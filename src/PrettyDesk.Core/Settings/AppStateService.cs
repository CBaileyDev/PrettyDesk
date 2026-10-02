namespace PrettyDesk.Core.Settings;

/// <summary>
/// Owns <c>state.json</c>. Saves are coalesced so a burst of rotation changes costs one write. A save can never throw to its
/// caller: the timer runs on a threadpool thread and a disk-full or antivirus lock must not take the tray down, so a failed
/// save is retried later instead.
/// </summary>
public sealed class AppStateService : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly JsonFileStore<AppState> _store;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private ITimer? _timer;
    private bool _disposed;

    public AppStateService(JsonFileStore<AppState> store, TimeProvider time)
    {
        _store = store;
        _time = time;
        Current = store.Load().Value;
    }

    public AppState Current { get; }

    /// <summary>True when the most recent save failed (the next retry is already scheduled).</summary>
    public bool LastSaveFailed { get; private set; }

    public void RequestSave() => Schedule(SaveDelay);

    /// <summary>Writes now. Returns false (and schedules a retry) instead of throwing when the write fails.</summary>
    public bool SaveNow()
    {
        lock (_gate)
        {
            try
            {
                _store.Save(Current);
                LastSaveFailed = false;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
            {
                // InvalidOperationException covers "collection was modified" when a writer mutates Current mid-serialise.
                LastSaveFailed = true;
                if (!_disposed)
                {
                    ScheduleLocked(RetryDelay);
                }

                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        SaveNow();
    }

    private void Schedule(TimeSpan delay)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                ScheduleLocked(delay);
            }
        }
    }

    private void ScheduleLocked(TimeSpan delay)
    {
        if (_timer is null)
        {
            _timer = _time.CreateTimer(_ => SaveNow(), null, delay, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }
}
