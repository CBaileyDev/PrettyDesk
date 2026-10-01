using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrettyDesk.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PrettyDesk.Windows;

/// <summary>
/// Foreground-window changes through <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c> with <c>WINEVENT_OUTOFCONTEXT</c>
/// (FR-DET-2): the callback runs in <em>our</em> process on our own message-loop thread, so nothing is injected into
/// any other process (FR-DET-3).
/// </summary>
public sealed class ForegroundWatcher : IForegroundSource, IDisposable
{
    private static ForegroundWatcher? Instance;

    private readonly ProcessDetailsSource _details;
    private readonly ProcessSource _processes;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private uint _threadId;
    private volatile ForegroundInfo? _current;

    public ForegroundWatcher(ProcessSource processes, ProcessDetailsSource details)
    {
        _processes = processes;
        _details = details;
    }

    public ForegroundInfo? Current => _current;

    public event Action? Changed;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        Instance = this;
        _thread = new Thread(Run) { IsBackground = true, Name = "PrettyDesk.WinEvents" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (_thread is null)
        {
            return;
        }

        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        Instance = null;
        _ready.Dispose();
    }

    private unsafe void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        using var hook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            null,
            &OnForegroundChanged,
            0,
            0,
            PInvoke.WINEVENT_OUTOFCONTEXT);

        Refresh();
        _ready.Set();

        MSG message;
        while (PInvoke.GetMessage(&message, default, 0, 0))
        {
            PInvoke.TranslateMessage(&message);
            PInvoke.DispatchMessage(&message);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnForegroundChanged(HWINEVENTHOOK hook, uint eventType, HWND hwnd, int idObject, int idChild, uint eventThread, uint eventTime) =>
        Instance?.Refresh();

    private unsafe void Refresh()
    {
        var window = PInvoke.GetForegroundWindow();
        if (window.IsNull)
        {
            _current = null;
            Changed?.Invoke();
            return;
        }

        uint rawPid;
        if (PInvoke.GetWindowThreadProcessId(window, &rawPid) == 0)
        {
            _current = null;
            Changed?.Invoke();
            return;
        }

        var pid = (int)rawPid;
        var exe = _details.TryGetImagePath(pid) is { } path
            ? Path.GetFileName(path)
            : ExeFromSnapshot(pid); // protected process: fall back to the exe name from the snapshot (FR-DET-3)

        _current = new ForegroundInfo(pid, exe);
        Changed?.Invoke();
    }

    private string? ExeFromSnapshot(int pid) => _processes.Snapshot().FirstOrDefault(p => p.Pid == pid)?.ExeName;
}
