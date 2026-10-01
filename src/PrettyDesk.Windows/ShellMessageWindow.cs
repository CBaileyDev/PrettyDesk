using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PrettyDesk.Windows;

/// <summary>
/// A hidden top-level window (not message-only, because message-only windows do not receive broadcasts) that listens for the
/// <c>TaskbarCreated</c> broadcast Explorer sends when it restarts (FR-APPLY-6).
/// </summary>
public sealed class ShellMessageWindow : IDisposable
{
    private const string ClassName = "PrettyDesk.ShellMessages";

    private static ShellMessageWindow? Instance;
    private static uint TaskbarCreatedMessage;

    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private uint _threadId;

    public event Action? TaskbarCreated;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        Instance = this;
        _thread = new Thread(Run) { IsBackground = true, Name = "PrettyDesk.ShellMessages" };
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
        TaskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
        var instance = PInvoke.GetModuleHandle((PCWSTR)null);

        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = (HINSTANCE)instance.Value,
                lpszClassName = className,
            };

            if (PInvoke.RegisterClassEx(&windowClass) == 0)
            {
                _ready.Set();
                return;
            }

            var window = PInvoke.CreateWindowEx(0, className, className, WINDOW_STYLE.WS_OVERLAPPED, 0, 0, 0, 0, default, default, (HINSTANCE)instance.Value, null);
            _ready.Set();

            MSG message;
            while (PInvoke.GetMessage(&message, default, 0, 0))
            {
                PInvoke.TranslateMessage(&message);
                PInvoke.DispatchMessage(&message);
            }

            if (!window.IsNull)
            {
                PInvoke.DestroyWindow(window);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (TaskbarCreatedMessage != 0 && message == TaskbarCreatedMessage)
        {
            Instance?.TaskbarCreated?.Invoke();
            return default;
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}

/// <summary>Session unlock and resume-from-sleep notifications (FR-WP-3 "on every unlock", FR-WP-6).</summary>
public sealed class SessionEvents : IDisposable
{
    public SessionEvents()
    {
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event Action? Unlocked;

    public event Action? Resumed;

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.SessionLogon)
        {
            Unlocked?.Invoke();
        }
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            Resumed?.Invoke();
        }
    }
}
