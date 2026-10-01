using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Rotation;
using PrettyDesk.Core.Settings;

namespace PrettyDesk.Core.Orchestration;

/// <summary>
/// The state machine of SPEC §5.4. Every input (game change, settings, monitors, content, clock) only marks the desired
/// state dirty; one serial consumer recomputes <em>desired</em> (monitor → render request), diffs it against what is
/// <em>applied</em>, and calls <see cref="IWallpaperSetter"/> only for the differences (FR-APPLY-4).
/// </summary>
public sealed partial class WallpaperOrchestrator : IWallpaperController, IAsyncDisposable
{
    /// <summary>Recompute debounce (SPEC §5.4).</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    /// <summary>Never apply more than once per monitor per this interval (FR-APPLY-4).</summary>
    public static readonly TimeSpan MinApplyGap = TimeSpan.FromSeconds(2);

    /// <summary>Display-change debounce (FR-MON-4).</summary>
    public static readonly TimeSpan DisplayChangeDebounce = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan DefaultPreviewDuration = TimeSpan.FromSeconds(15);

    private readonly IMonitorProvider _monitors;
    private readonly IWallpaperSetter _setter;
    private readonly IWallpaperRenderer _renderer;
    private readonly IContentLibrary _content;
    private readonly ISystemState _system;
    private readonly ICatalogProvider _catalog;
    private readonly ISettingsProvider _settings;
    private readonly RotationScheduler _scheduler;
    private readonly AppStateService? _state;
    private readonly IWallpaperBackup? _backup;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, string> _applied = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastApply = new(StringComparer.Ordinal);

    private ActiveGame? _activeGame;
    private volatile bool _advanceRequested;
    private volatile bool _forceReapply;
    private (string WallpaperId, DateTimeOffset Until)? _preview;
    private string? _lastGameContext;
    private OrchestratorMode? _lastMode;
    private string? _lastGameId;
    private int _failures;
    private bool _backupEnsured;
    private ITimer? _wakeTimer;
    private ITimer? _monitorTimer;
    private Task? _loop;
    private bool _started;

    public WallpaperOrchestrator(
        IMonitorProvider monitors,
        IWallpaperSetter setter,
        IWallpaperRenderer renderer,
        IContentLibrary content,
        ISystemState system,
        ICatalogProvider catalog,
        ISettingsProvider settings,
        RotationScheduler scheduler,
        AppStateService? state,
        TimeProvider time,
        ILogger<WallpaperOrchestrator> logger,
        IWallpaperBackup? backup = null)
    {
        _backup = backup;
        _monitors = monitors;
        _setter = setter;
        _renderer = renderer;
        _content = content;
        _system = system;
        _catalog = catalog;
        _settings = settings;
        _scheduler = scheduler;
        _state = state;
        _time = time;
        _logger = logger;
        Status = new OrchestratorStatus();
    }

    public OrchestratorStatus Status { get; private set; }

    public event Action<OrchestratorStatus>? StatusChanged;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _monitors.Changed += OnMonitorsChanged;
        _system.Changed += OnInputChanged;
        _settings.Changed += OnInputChanged;
        _catalog.Changed += OnInputChanged;
        _content.PackChanged += OnPackChanged;
        _scheduler.StateChanged += OnSchedulerChanged;
        _loop = Task.Run(RunAsync);
        Invalidate(immediate: true);
    }

    // ---- inputs -----------------------------------------------------------------------------------------------

    public void SetActiveGame(ActiveGame? game)
    {
        _activeGame = game;
        Invalidate();
    }

    /// <summary>Manual "Next wallpaper" for whatever context is current (FR-WP-7).</summary>
    public void NextWallpaper()
    {
        _advanceRequested = true;
        Invalidate(immediate: true);
    }

    public void Pause(TimeSpan? duration)
    {
        var until = duration is null ? (DateTimeOffset?)null : _time.GetUtcNow() + duration.Value;
        _settings.Update(s =>
        {
            s.General.Paused = true;
            s.General.PauseUntil = until;
        });
    }

    public void Resume() => _settings.Update(s =>
    {
        s.General.Paused = false;
        s.General.PauseUntil = null;
    });

    /// <summary>Shows a wallpaper on every monitor for <paramref name="duration"/> (default 15 s), then restores (FR-WP-10).</summary>
    public void Preview(string wallpaperId, TimeSpan? duration = null)
    {
        _preview = (wallpaperId, _time.GetUtcNow() + (duration ?? DefaultPreviewDuration));
        Invalidate(immediate: true);
    }

    public void CancelPreview()
    {
        _preview = null;
        Invalidate(immediate: true);
    }

    /// <summary>Re-evaluates soon without forcing a re-apply (e.g. after resume from sleep).</summary>
    public void Poke() => Invalidate(immediate: false);

    public void NotifyUnlock()
    {
        _scheduler.NotifyUnlock();
        Invalidate(immediate: true);
    }

    /// <summary>Explorer restarted (TaskbarCreated) or the session resumed: forget what we believed was applied (FR-APPLY-6).</summary>
    public void NotifyDesktopReset()
    {
        _applied.Clear();
        _lastApply.Clear();
        _forceReapply = true;
        Invalidate(immediate: true);
    }

    // ---- the reconcile pass -----------------------------------------------------------------------------------

    /// <summary>One evaluation: desired state → diff → apply. Public so hosts and tests can drive it deterministically.</summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var settings = _settings.Current;
        var catalog = _catalog.Current;
        var monitors = _monitors.GetMonitors();
        DateTimeOffset? wakeAt = null;

        if (_system.IsWallpaperPolicyLocked)
        {
            Publish(new OrchestratorStatus { Mode = OrchestratorMode.Blocked }, wakeAt: null);
            return;
        }

        if (settings.General.Paused && settings.General.PauseUntil is { } resumeAt && now >= resumeAt)
        {
            _settings.Update(s =>
            {
                s.General.Paused = false;
                s.General.PauseUntil = null;
            });
            settings = _settings.Current;
        }

        if (settings.General.Paused)
        {
            Publish(new OrchestratorStatus { Mode = OrchestratorMode.Paused, PausedUntil = settings.General.PauseUntil, WallpaperByMonitor = CurrentIds(monitors) }, settings.General.PauseUntil);
            return;
        }

        if (monitors.Count == 0)
        {
            return;
        }

        var failed = false;
        if (_preview is { } preview)
        {
            if (now < preview.Until)
            {
                failed = await ApplyPreviewAsync(preview.WallpaperId, monitors, now, cancellationToken);
                Publish(new OrchestratorStatus { Mode = OrchestratorMode.Preview, ApplyFailed = failed, WallpaperByMonitor = monitors.ToDictionary(m => m.Id, _ => preview.WallpaperId) }, preview.Until);
                return;
            }

            _preview = null;
        }

        var registry = GameRegistry.Build(catalog, settings);
        var game = _activeGame is null ? null : registry.Find(_activeGame.GameId);
        var gamePlan = game is null ? null : ContextPlanner.PlanGame(game, settings, catalog, _content);

        if (game is not null && gamePlan is { Pool.Count: 0 })
        {
            if (game.PackId is { } packId)
            {
                _content.RequestPack(packId, monitors);
            }

            // Stay on whatever is showing until the pack arrives (SPEC §5.4).
            Publish(new OrchestratorStatus { Mode = OrchestratorMode.GameLoading, GameId = game.Id, GameName = game.DisplayName, WallpaperByMonitor = CurrentIds(monitors) }, wakeAt: null);
            return;
        }

        var defaultPlan = ContextPlanner.PlanDefault(settings, catalog, _content, _system);
        var primaryId = (monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0]).Id;
        var gameMonitors = gamePlan is null
            ? []
            : settings.Monitors.GameOnSecondaryOnly && monitors.Count > 1 ? monitors.Where(m => m.Id != primaryId).ToList() : monitors.ToList();
        var defaultMonitors = monitors.Where(m => gameMonitors.All(g => g.Id != m.Id)).ToList();

        var advance = _advanceRequested;
        _advanceRequested = false;
        var assignments = new List<(MonitorInfo Monitor, string WallpaperId)>();
        OrchestratorStatus? status = null;

        if (gamePlan is not null && game is not null)
        {
            var entering = _lastGameContext != gamePlan.ContextKey;
            _lastGameContext = gamePlan.ContextKey;
            ResolveInto(gamePlan, gameMonitors, settings, entering, advance, assignments);
            wakeAt = Earliest(wakeAt, _scheduler.NextDeadline(gamePlan.ContextKey, gamePlan.Policy));
            status = new OrchestratorStatus
            {
                Mode = _activeGame!.InGrace ? OrchestratorMode.GameGrace : OrchestratorMode.Game,
                GameId = game.Id,
                GameName = game.DisplayName,
                IsRotating = !gamePlan.IsFixed && gamePlan.Pool.Count > 1,
                PoolSize = gamePlan.Pool.Count,
                PoolIndex = Math.Max(0, gamePlan.Pool.ToList().IndexOf(assignments.FirstOrDefault().WallpaperId ?? string.Empty)),
            };
        }
        else
        {
            _lastGameContext = null;
        }

        if (defaultMonitors.Count > 0)
        {
            if (defaultPlan.Pool.Count == 0)
            {
                status ??= new OrchestratorStatus { Mode = OrchestratorMode.Default, DefaultTitle = defaultPlan.Title, NoContent = true, WallpaperByMonitor = CurrentIds(monitors) };
            }
            else
            {
                ResolveInto(defaultPlan, defaultMonitors, settings, entering: false, advance && gamePlan is null, assignments);
                wakeAt = Earliest(wakeAt, _scheduler.NextDeadline(defaultPlan.ContextKey, defaultPlan.Policy));
                status ??= new OrchestratorStatus
                {
                    Mode = OrchestratorMode.Default,
                    DefaultTitle = defaultPlan.Title,
                    IsRotating = !defaultPlan.IsFixed && defaultPlan.Pool.Count > 1,
                    PoolSize = defaultPlan.Pool.Count,
                    PoolIndex = Math.Max(0, defaultPlan.Pool.ToList().IndexOf(assignments.FirstOrDefault().WallpaperId ?? string.Empty)),
                };
            }
        }

        if (!_backupEnsured && _backup is not null && assignments.Count > 0)
        {
            // FR-RESTORE-1: the original wallpaper is snapshotted before the very first apply, and a failure here must
            // stop us from overwriting it.
            try
            {
                await _backup.EnsureBackupAsync(cancellationToken);
                _backupEnsured = true;
            }
#pragma warning disable CA1031 // NFR-13: reported as a failed apply and retried with backoff.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogBackupFailed(ex);
                _failures++;
                Publish((status ?? new OrchestratorStatus()) with { ApplyFailed = true, WallpaperByMonitor = CurrentIds(monitors) }, now + FailureBackoff(_failures));
                return;
            }
        }

        var applied = new Dictionary<string, string>();
        DateTimeOffset? retryAt = null;
        foreach (var (monitor, wallpaperId) in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await ApplyToMonitorAsync(monitor, wallpaperId, now, cancellationToken);
            applied[monitor.Id] = wallpaperId;
            failed |= outcome.Failed;
            retryAt = Earliest(retryAt, outcome.RetryAt);
        }

        // Forget monitors that were unplugged so a re-plug applies fresh.
        foreach (var gone in _applied.Keys.Where(k => monitors.All(m => m.Id != k)).ToList())
        {
            _applied.Remove(gone);
            _lastApply.Remove(gone);
        }

        _forceReapply = _forceReapply && failed;
        _failures = failed ? _failures + 1 : 0;
        if (failed)
        {
            retryAt = Earliest(retryAt, now + FailureBackoff(_failures));
        }

        status ??= new OrchestratorStatus { Mode = OrchestratorMode.Default };
        Publish(status with { WallpaperByMonitor = applied, ApplyFailed = failed, NextChange = wakeAt }, Earliest(wakeAt, retryAt));
        _state?.RequestSave();
    }

    private void ResolveInto(
        ContextPlan plan,
        List<MonitorInfo> group,
        AppSettings settings,
        bool entering,
        bool advance,
        List<(MonitorInfo, string)> assignments)
    {
        if (plan.IsFixed)
        {
            foreach (var monitor in group)
            {
                assignments.Add((monitor, plan.Pool[0]));
            }

            return;
        }

        var slots = settings.Monitors.Mode == MonitorMode.Different ? group.Count : 1;
        var result = advance
            ? _scheduler.Advance(plan.ContextKey, plan.Pool, plan.Policy, slots)
            : entering
                ? _scheduler.EnterContext(plan.ContextKey, plan.Pool, plan.Policy, slots)
                : _scheduler.Resolve(plan.ContextKey, plan.Pool, plan.Policy, slots);

        for (var i = 0; i < group.Count; i++)
        {
            assignments.Add((group[i], result.Ids[Math.Min(i, result.Ids.Count - 1)]));
        }

    }

    private async Task<ApplyOutcome> ApplyToMonitorAsync(MonitorInfo monitor, string wallpaperId, DateTimeOffset now, CancellationToken ct)
    {
        var asset = _content.TryGetAsset(wallpaperId);
        if (asset is null)
        {
            return ApplyOutcome.Ok;
        }

        try
        {
            var path = await _renderer.RenderAsync(asset, monitor, ct);
            if (!await NeedsApplyAsync(monitor.Id, path, ct))
            {
                return ApplyOutcome.Ok;
            }

            if (_lastApply.TryGetValue(monitor.Id, out var last) && now - last < MinApplyGap)
            {
                // Coalesce bursts: come back once the gap has elapsed.
                return new ApplyOutcome(false, last + MinApplyGap);
            }

            await _setter.SetAsync(monitor.Id, path, ct);
            _applied[monitor.Id] = path;
            _lastApply[monitor.Id] = now;
            if (_state is not null)
            {
                _state.Current.LastApplied[monitor.Id] = path;
            }

            return ApplyOutcome.Ok;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // NFR-13: a failed apply (COM hiccup, render error) must never crash the tray; it is logged and retried with backoff.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogApplyFailed(wallpaperId, ex);
            return new ApplyOutcome(true, null);
        }
    }

    private async Task<bool> ApplyPreviewAsync(string wallpaperId, IReadOnlyList<MonitorInfo> monitors, DateTimeOffset now, CancellationToken ct)
    {
        var failed = false;
        foreach (var monitor in monitors)
        {
            var outcome = await ApplyToMonitorAsync(monitor, wallpaperId, now, ct);
            failed |= outcome.Failed;
        }

        return failed;
    }

    private async Task<bool> NeedsApplyAsync(string monitorId, string path, CancellationToken ct)
    {
        if (_forceReapply)
        {
            return true;
        }

        if (_applied.TryGetValue(monitorId, out var known))
        {
            return !string.Equals(known, path, StringComparison.OrdinalIgnoreCase);
        }

        // First sight of this monitor: ask Windows what is really there (FR-APPLY-4 idempotence across restarts).
        var current = await _setter.GetAsync(monitorId, ct);
        if (string.Equals(current, path, StringComparison.OrdinalIgnoreCase))
        {
            _applied[monitorId] = path;
            return false;
        }

        return true;
    }

    private Dictionary<string, string> CurrentIds(IReadOnlyList<MonitorInfo> monitors)
    {
        var map = new Dictionary<string, string>();
        foreach (var monitor in monitors)
        {
            if (Status.WallpaperByMonitor.TryGetValue(monitor.Id, out var id))
            {
                map[monitor.Id] = id;
            }
        }

        return map;
    }

    private static TimeSpan FailureBackoff(int failures) =>
        TimeSpan.FromSeconds(Math.Min(60, 2 * Math.Pow(2, Math.Min(failures - 1, 5))));

    private static DateTimeOffset? Earliest(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;

    private void Publish(OrchestratorStatus status, DateTimeOffset? wakeAt)
    {
        ScheduleWake(wakeAt);

        if (status.Mode != _lastMode || status.GameId != _lastGameId)
        {
            LogTransition(_lastMode, status.Mode, status.GameId);
            _lastMode = status.Mode;
            _lastGameId = status.GameId;
        }

        if (status.SameAs(Status))
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke(status);
    }

    // ---- plumbing ---------------------------------------------------------------------------------------------

    private void Invalidate(bool immediate = false) => _signals.Writer.TryWrite(immediate);

    private void OnInputChanged() => Invalidate();

    private void OnPackChanged(string packId)
    {
        _ = packId;
        Invalidate();
    }

    private void OnSchedulerChanged() => _state?.RequestSave();

    private void OnMonitorsChanged()
    {
        if (_monitorTimer is null)
        {
            _monitorTimer = _time.CreateTimer(_ => Invalidate(immediate: true), null, DisplayChangeDebounce, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _monitorTimer.Change(DisplayChangeDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void ScheduleWake(DateTimeOffset? at)
    {
        if (at is null)
        {
            _wakeTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var due = at.Value - _time.GetUtcNow();
        if (due < TimeSpan.Zero)
        {
            due = TimeSpan.Zero;
        }

        if (_wakeTimer is null)
        {
            _wakeTimer = _time.CreateTimer(_ => Invalidate(immediate: true), null, due, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _wakeTimer.Change(due, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (await _signals.Reader.WaitToReadAsync(_cts.Token))
            {
                var immediate = false;
                while (_signals.Reader.TryRead(out var item))
                {
                    immediate |= item;
                }

                if (!immediate)
                {
                    await Task.Delay(Debounce, _time, _cts.Token);
                    while (_signals.Reader.TryRead(out _))
                    {
                    }
                }

                try
                {
                    await ReconcileAsync(_cts.Token);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    throw;
                }
#pragma warning disable CA1031 // NFR-13: the consumer loop must survive any single failed pass.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogReconcileFailed(ex);
                    ScheduleWake(_time.GetUtcNow() + TimeSpan.FromSeconds(10));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_started)
        {
            _monitors.Changed -= OnMonitorsChanged;
            _system.Changed -= OnInputChanged;
            _settings.Changed -= OnInputChanged;
            _catalog.Changed -= OnInputChanged;
            _content.PackChanged -= OnPackChanged;
            _scheduler.StateChanged -= OnSchedulerChanged;
        }

        _signals.Writer.TryComplete();
        if (_wakeTimer is not null)
        {
            await _wakeTimer.DisposeAsync();
        }

        if (_monitorTimer is not null)
        {
            await _monitorTimer.DisposeAsync();
        }

        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }

        _cts.Dispose();
    }

    private readonly record struct ApplyOutcome(bool Failed, DateTimeOffset? RetryAt)
    {
        public static ApplyOutcome Ok => new(false, null);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Orchestrator {Previous} -> {Current} (game: {GameId})")]
    private partial void LogTransition(OrchestratorMode? previous, OrchestratorMode current, string? gameId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Applying wallpaper {WallpaperId} failed; will retry with backoff")]
    private partial void LogApplyFailed(string wallpaperId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not back up the original wallpaper; not applying until it can be saved")]
    private partial void LogBackupFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconcile pass failed; retrying shortly")]
    private partial void LogReconcileFailed(Exception ex);
}
