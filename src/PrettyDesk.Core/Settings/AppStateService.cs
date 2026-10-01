namespace PrettyDesk.Core.Settings;

/// <summary>Owns <c>state.json</c>. Saves are coalesced so a burst of rotation changes costs one write.</summary>
public sealed class AppStateService : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    private readonly JsonFileStore<AppState> _store;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private ITimer? _timer;

    public AppStateService(JsonFileStore<AppState> store, TimeProvider time)
    {
        _store = store;
        _time = time;
        Current = store.Load().Value;
    }

    public AppState Current { get; }

    public void RequestSave()
    {
        lock (_gate)
        {
            if (_timer is null)
            {
                _timer = _time.CreateTimer(_ => SaveNow(), null, SaveDelay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void SaveNow()
    {
        lock (_gate)
        {
            _store.Save(Current);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        SaveNow();
    }
}
