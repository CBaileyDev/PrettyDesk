using Microsoft.Win32;
using PrettyDesk.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace PrettyDesk.Windows;

/// <summary>
/// Watches <c>HKCU\Software\Valve\Steam\RunningAppID</c> (a DWORD, 0 when nothing runs) with
/// <c>RegNotifyChangeKeyValue</c> instead of polling (FR-DET-1). If Steam is not installed it re-checks every 30 s, which is
/// a single registry open.
/// </summary>
public sealed class SteamRunningAppWatcher : ISteamRunningAppSource, IDisposable
{
    private const string KeyPath = @"Software\Valve\Steam";
    private const string ValueName = "RunningAppID";

    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;
    private int _current;
    private int _disposed;

    public uint CurrentAppId => (uint)Volatile.Read(ref _current);

    public event Action? Changed;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "PrettyDesk.SteamWatch" };
        _thread.Start();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _stop.Dispose();
    }

    private void Run()
    {
        try
        {
            Watch();
        }
#pragma warning disable CA1031 // NFR-13: an exception on this thread (key deleted mid-read, a bad subscriber, Dispose race) must not crash the process.
        catch (Exception)
#pragma warning restore CA1031
        {
            // detection falls back to process polling when Steam's AppID signal is lost
        }
    }

    private void Watch()
    {
        while (!_stop.WaitOne(0))
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key is null)
            {
                Publish(0);
                if (_stop.WaitOne(TimeSpan.FromSeconds(30)))
                {
                    return;
                }

                continue;
            }

            using var changed = new ManualResetEvent(false);
            while (!_stop.WaitOne(0))
            {
                Publish(key.GetValue(ValueName) is int value ? value : 0);
                changed.Reset();
                var result = PInvoke.RegNotifyChangeKeyValue(
                    new HKEY(key.Handle.DangerousGetHandle()),
                    false,
                    REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET,
                    new HANDLE(changed.SafeWaitHandle.DangerousGetHandle()),
                    true);

                if (result != WIN32_ERROR.ERROR_SUCCESS)
                {
                    break;
                }

                if (WaitHandle.WaitAny([changed, _stop]) == 1)
                {
                    return;
                }
            }
        }
    }

    private void Publish(int value)
    {
        if (Interlocked.Exchange(ref _current, value) != value)
        {
            Changed?.Invoke();
        }
    }
}
