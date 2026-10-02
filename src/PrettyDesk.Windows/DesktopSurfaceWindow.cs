using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PrettyDesk.Windows;

/// <summary>Styles only the application's own desktop surface; never touches an Explorer or game window.</summary>
public static partial class DesktopSurfaceWindow
{
    public static void Configure(nint ownWindow)
    {
        var style = GetWindowLongPtr(ownWindow, -20);
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(ownWindow, -20, style | 0x08000000 | 0x00000020 | 0x00000080);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public static void Place(nint ownWindow, int x, int y)
    {
        if (!SetWindowPos(ownWindow, 0, x, y, 0, 0, 0x0010 | 0x0001 | 0x0004))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint window, int index, nint value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
