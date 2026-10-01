using Microsoft.Extensions.Logging;

namespace PrettyDesk.Presentation.Services;

/// <summary>A release the backend can install. <see cref="Token"/> is opaque to the coordinator (the backend's own handle).</summary>
public sealed record AvailableUpdate(string Version, object Token);

/// <summary>The packaging-specific half of updating (Velopack in the shipped app), kept behind an interface so the policy is testable.</summary>
public interface IUpdateBackend
{
    /// <summary>False for portable zips and dev builds, which cannot update themselves.</summary>
    bool IsSupported { get; }

    /// <summary>A release downloaded in an earlier session that has not been applied yet.</summary>
    AvailableUpdate? PendingRestart { get; }

    Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken);

    Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellationToken);

    void ApplyAndRestart(AvailableUpdate update);
}

/// <summary>
/// The update policy (SPEC §9): check shortly after startup and every 12 hours, download in the background, and install on the
/// next start, or right away when the user chooses "Restart to update". Every failure is quiet and non-fatal (NFR-7): the state
/// becomes <see cref="UpdateStateKind.Failed"/> and the next scheduled check tries again.
/// </summary>
public sealed partial class UpdateCoordinator : IUpdateService, IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly IUpdateBackend _backend;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdateCoordinator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private ITimer? _timer;
    private AvailableUpdate? _ready;
    private UpdateState _state;
    private int _disposed;

    public UpdateCoordinator(IUpdateBackend backend, TimeProvider time, ILogger<UpdateCoordinator> logger)
    {
        _backend = backend;
        _time = time;
        _logger = logger;

        if (!backend.IsSupported)
        {
            _state = new UpdateState(UpdateStateKind.NotSupported);
        }
        else if (backend.PendingRestart is { } pending)
        {
            _ready = pending;
            _state = new UpdateState(UpdateStateKind.ReadyToInstall, pending.Version);
        }
        else
        {
            _state = new UpdateState(UpdateStateKind.Idle);
        }
    }

    public UpdateState State => Volatile.Read(ref _state);

    public event Action? Changed;

    /// <summary>Starts the schedule: first check after <see cref="StartupDelay"/>, then every <see cref="CheckInterval"/>.</summary>
    public void Start()
    {
        if (!_backend.IsSupported)
        {
            return;
        }

        _timer ??= _time.CreateTimer(_ => _ = CheckAsync(_cts.Token), null, StartupDelay, CheckInterval);
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_backend.IsSupported)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_ready is not null)
            {
                return; // already downloaded; nothing newer is worth fetching before the restart
            }

            SetState(new UpdateState(UpdateStateKind.Checking));
            var update = await _backend.CheckAsync(cancellationToken);
            if (update is null)
            {
                SetState(new UpdateState(UpdateStateKind.UpToDate));
                return;
            }

            SetState(new UpdateState(UpdateStateKind.Available, update.Version));
            var lastPercent = -1;
            SetState(new UpdateState(UpdateStateKind.Downloading, update.Version, 0));
            await _backend.DownloadAsync(update, percent =>
            {
                percent = Math.Clamp(percent, 0, 100);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    SetState(new UpdateState(UpdateStateKind.Downloading, update.Version, percent));
                }
            }, cancellationToken);

            _ready = update;
            SetState(new UpdateState(UpdateStateKind.ReadyToInstall, update.Version));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _cts.IsCancellationRequested)
        {
            SetState(new UpdateState(UpdateStateKind.Idle));
        }
#pragma warning disable CA1031 // NFR-7: any backend failure (network, disk, feed format) must stay quiet and retry at the next check.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCheckFailed(ex);
            SetState(new UpdateState(UpdateStateKind.Failed));
        }
        finally
        {
            _gate.Release();
        }
    }

    public void ApplyAndRestart()
    {
        if (_ready is { } update && State.Kind == UpdateStateKind.ReadyToInstall)
        {
            _backend.ApplyAndRestart(update);
        }
    }

    public void Dispose()
    {
        // Idempotent: registered under two DI service types and also disposed explicitly by AppRuntime.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        _timer?.Dispose();
        _cts.Dispose();
        _gate.Dispose();
    }

    private void SetState(UpdateState state)
    {
        Volatile.Write(ref _state, state);
        Changed?.Invoke();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check failed; will retry at the next scheduled check.")]
    private partial void LogCheckFailed(Exception ex);
}
