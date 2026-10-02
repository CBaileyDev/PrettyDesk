namespace PrettyDesk.App;

/// <summary>
/// One PrettyDesk per Windows session (FR-APP-1): a named mutex decides who is first; a second launch signals the first to
/// open its window and exits. Names are session-local, so fast user switching works.
/// </summary>
public sealed class SingleInstance : IDisposable
{
#if PRETTYDESK_ACCEPTANCE
    private static readonly string AcceptanceScope = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(AcceptanceHarness.DataRoot)))[..12];
    private static readonly string MutexName = @"Local\PrettyDesk.Acceptance.SingleInstance." + AcceptanceScope;
    private static readonly string ShowEventName = @"Local\PrettyDesk.Acceptance.ShowWindow." + AcceptanceScope;
#else
    private const string MutexName = @"Local\PrettyDesk.SingleInstance";
    private const string ShowEventName = @"Local\PrettyDesk.ShowWindow";
#endif

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _listener;
    private int _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    /// <summary>Returns the instance when this process is the first; otherwise signals the first one and returns null.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (created)
        {
            return new SingleInstance(mutex, showEvent);
        }

        showEvent.Set();
        showEvent.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>Invokes <paramref name="onShowRequested"/> (on a background thread) whenever another launch asks us to show the window.</summary>
    public void Listen(Action onShowRequested)
    {
        _listener = new Thread(() =>
        {
            var handles = new WaitHandle[] { _showEvent, _stop };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                onShowRequested();
            }
        })
        {
            IsBackground = true,
            Name = "PrettyDesk.SingleInstance",
        };
        _listener.Start();
    }

    public void Dispose()
    {
        // Quit disposes this explicitly and Program's `using` disposes it again; the second call must be a no-op.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Set();
        _listener?.Join(TimeSpan.FromSeconds(1));
        _showEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // not owned by this thread: the OS releases it when the process exits
        }

        _mutex.Dispose();
        _stop.Dispose();
    }
}
