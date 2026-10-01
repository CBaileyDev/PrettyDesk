using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PrettyDesk.Windows;

public sealed record WindowedApp(string ExeName, string Title);

/// <summary>
/// Apps with a visible, titled top-level window, for "Add a game" (FR-CUSTOM-1). It reads window titles and the owning
/// process's exe name from a Toolhelp snapshot only: no process handle is opened (FR-DET-3), and the list is shown to the
/// user and never stored or sent.
/// </summary>
public static class WindowedApps
{
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "ApplicationFrameHost.exe", "TextInputHost.exe", "SearchHost.exe", "StartMenuExperienceHost.exe",
        "ShellExperienceHost.exe", "LockApp.exe", "SystemSettings.exe", "PrettyDesk.exe", "dwm.exe",
    };

    public static unsafe IReadOnlyList<WindowedApp> Enumerate(ProcessSource processes)
    {
        var titles = new Dictionary<uint, string>();
        var gc = GCHandle.Alloc(titles);
        try
        {
            PInvoke.EnumWindows(&EnumCallback, (LPARAM)GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        var names = processes.Snapshot().GroupBy(p => (uint)p.Pid).ToDictionary(g => g.Key, g => g.First().ExeName);
        return titles
            .Where(kv => names.ContainsKey(kv.Key) && !Hidden.Contains(names[kv.Key]))
            .Select(kv => new WindowedApp(names[kv.Key], kv.Value))
            .GroupBy(a => a.ExeName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe BOOL EnumCallback(HWND hwnd, LPARAM lParam)
    {
        var titles = (Dictionary<uint, string>)GCHandle.FromIntPtr(lParam.Value).Target!;
        uint pid;
        if (PInvoke.GetWindowThreadProcessId(hwnd, &pid) == 0 || !PInvoke.IsWindowVisible(hwnd) || titles.ContainsKey(pid))
        {
            return true;
        }

        var length = PInvoke.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return true;
        }

        var buffer = new char[length + 1];
        fixed (char* p = buffer)
        {
            var copied = PInvoke.GetWindowText(hwnd, p, buffer.Length);
            if (copied > 0)
            {
                titles[pid] = new string(buffer, 0, copied);
            }
        }

        return true;
    }
}
