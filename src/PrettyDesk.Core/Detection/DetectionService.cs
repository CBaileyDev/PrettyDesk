using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PrettyDesk.Core.Abstractions;

namespace PrettyDesk.Core.Detection;

/// <summary>What the last evaluation saw. Displayed live in Settings → Advanced and never written to disk (SPEC §7).</summary>
public sealed record DetectionObservation(string? ForegroundExe, IReadOnlyList<string> MatchedGameIds);

public sealed record DetectionConfiguration(
    bool Enabled,
    TimeSpan PollInterval,
    DetectionOptions Options,
    RuleMatcher Matcher);

/// <summary>
/// Glues the sources to <see cref="GameSessionTracker"/>. Polls on an interval (FR-DET-2), reacts immediately to
/// foreground and Steam changes, and wakes itself at the tracker's next deadline. Every trigger funnels into one
/// serial consumer so evaluation never races (SPEC §5.5).
/// </summary>
public sealed partial class DetectionService : IDetectionFeed, IAsyncDisposable
{
    private readonly IProcessSource _processes;
    private readonly IForegroundSource? _foreground;
    private readonly ISteamRunningAppSource? _steam;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly GameSessionTracker _tracker;
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource _cts = new();
    private readonly object _configLock = new();

    private DetectionConfiguration _config;
    private ITimer? _pollTimer;
    private ITimer? _deadlineTimer;
    private Task? _loop;

    public DetectionService(
        IProcessSource processes,
        IForegroundSource? foreground,
        ISteamRunningAppSource? steam,
        DetectionConfiguration configuration,
        TimeProvider time,
        ILogger<DetectionService> logger)
    {
        _processes = processes;
        _foreground = foreground;
        _steam = steam;
        _config = configuration;
        _time = time;
        _logger = logger;
        _tracker = new GameSessionTracker(configuration.Options);
        _tracker.ActiveChanged += OnActiveChanged;
    }

    public ActiveGame? Active => _tracker.Active;

    public event Action<ActiveGameChange>? ActiveChanged;

    public event Action<DetectionObservation>? Observed;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        if (_foreground is not null)
        {
            _foreground.Changed += Signal;
        }

        if (_steam is not null)
        {
            _steam.Changed += Signal;
        }

        _pollTimer = _time.CreateTimer(_ => Signal(), null, TimeSpan.Zero, _config.PollInterval);
        _loop = Task.Run(RunAsync);
    }

    public void Reconfigure(DetectionConfiguration configuration)
    {
        lock (_configLock)
        {
            _config = configuration;
        }

        _tracker.UpdateOptions(configuration.Options);
        _pollTimer?.Change(TimeSpan.Zero, configuration.PollInterval);
        Signal();
    }

    /// <summary>One synchronous evaluation: snapshot → match → tracker. Exposed for deterministic tests.</summary>
    public void EvaluateOnce()
    {
        DetectionConfiguration config;
        lock (_configLock)
        {
            config = _config;
        }

        if (!config.Enabled)
        {
            _tracker.Reset();
            ScheduleDeadline();
            return;
        }

        var processes = _processes.Snapshot();
        var foreground = _foreground?.Current;
        var matches = config.Matcher.Match(processes, _steam?.CurrentAppId ?? 0);
        _tracker.Observe(_time.GetUtcNow(), matches, foreground?.Pid ?? 0);
        ScheduleDeadline();
        Observed?.Invoke(new DetectionObservation(foreground?.ExeName, matches.Select(m => m.GameId).ToList()));
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_foreground is not null)
        {
            _foreground.Changed -= Signal;
        }

        if (_steam is not null)
        {
            _steam.Changed -= Signal;
        }

        if (_pollTimer is not null)
        {
            await _pollTimer.DisposeAsync();
        }

        if (_deadlineTimer is not null)
        {
            await _deadlineTimer.DisposeAsync();
        }

        _signals.Writer.TryComplete();
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }

        _cts.Dispose();
    }

    private void Signal() => _signals.Writer.TryWrite(true);

    private async Task RunAsync()
    {
        try
        {
            while (await _signals.Reader.WaitToReadAsync(_cts.Token))
            {
                while (_signals.Reader.TryRead(out _))
                {
                }

                try
                {
                    EvaluateOnce();
                }
#pragma warning disable CA1031 // NFR-13: no unhandled exception may take the tray down; the error is logged and the loop continues.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogEvaluationFailed(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private void ScheduleDeadline()
    {
        var deadline = _tracker.NextDeadline;
        if (deadline is null)
        {
            _deadlineTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var due = deadline.Value - _time.GetUtcNow();
        if (due < TimeSpan.Zero)
        {
            due = TimeSpan.Zero;
        }

        if (_deadlineTimer is null)
        {
            _deadlineTimer = _time.CreateTimer(_ => Signal(), null, due, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _deadlineTimer.Change(due, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnActiveChanged(ActiveGameChange change)
    {
        LogGameChanged(change.Previous?.GameId ?? "none", change.Previous?.InGrace ?? false, change.Current?.GameId ?? "none", change.Current?.InGrace ?? false);
        ActiveChanged?.Invoke(change);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Active game {Previous} (grace: {PreviousGrace}) -> {Current} (grace: {CurrentGrace})")]
    private partial void LogGameChanged(string previous, bool previousGrace, string current, bool currentGrace);

    [LoggerMessage(Level = LogLevel.Error, Message = "Detection evaluation failed; will retry on the next trigger")]
    private partial void LogEvaluationFailed(Exception ex);
}
