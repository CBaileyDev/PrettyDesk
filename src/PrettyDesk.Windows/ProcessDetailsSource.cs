using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrettyDesk.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace PrettyDesk.Windows;

/// <summary>
/// Lazy lookups for candidate processes only. The single <c>OpenProcess</c> call below requests
/// <c>PROCESS_QUERY_LIMITED_INFORMATION</c> and nothing else (FR-DET-3, enforced by a source guard test). Access-denied is
/// handled silently by returning null.
/// </summary>
public sealed class ProcessDetailsSource : IProcessDetailsSource
{
    public unsafe string? TryGetImagePath(int pid)
    {
        var handle = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle.IsNull)
        {
            return null;
        }

        try
        {
            Span<char> buffer = stackalloc char[1024];
            var size = (uint)buffer.Length;
            fixed (char* p = buffer)
            {
                return PInvoke.QueryFullProcessImageName(handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, p, &size)
                    ? new string(buffer[..(int)size])
                    : null;
            }
        }
        finally
        {
            PInvoke.CloseHandle(handle);
        }
    }

    /// <summary>Title of the first visible top-level window owned by the process. Reads only window text; never touches the process.</summary>
    public unsafe string? TryGetMainWindowTitle(int pid)
    {
        var search = new TitleSearch((uint)pid);
        var gc = GCHandle.Alloc(search);
        try
        {
            PInvoke.EnumWindows(&EnumCallback, (LPARAM)GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        return search.Title;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe BOOL EnumCallback(HWND hwnd, LPARAM lParam)
    {
        var search = (TitleSearch)GCHandle.FromIntPtr(lParam.Value).Target!;
        uint owner;
        if (PInvoke.GetWindowThreadProcessId(hwnd, &owner) == 0 || owner != search.Pid || !PInvoke.IsWindowVisible(hwnd))
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
                search.Title = new string(buffer, 0, copied);
                return false;
            }
        }

        return true;
    }

    private sealed class TitleSearch(uint pid)
    {
        public uint Pid { get; } = pid;

        public string? Title { get; set; }
    }
}
