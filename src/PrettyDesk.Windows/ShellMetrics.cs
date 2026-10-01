using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PrettyDesk.Windows;

public static class ShellMetrics
{
    /// <summary>The pixel size of a notification-area icon at the system DPI (16 px at 100%, 24 px at 150%, 32 px at 200%…).</summary>
    public static int SmallIconSizePixels()
    {
        var dpi = PInvoke.GetDpiForSystem();
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi);
        return size > 0 ? size : 16;
    }
}
