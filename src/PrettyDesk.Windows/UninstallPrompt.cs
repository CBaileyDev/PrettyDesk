using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PrettyDesk.Windows;

/// <summary>
/// A plain native Yes/No box for the uninstall hook, which runs in a bare process without WPF or a dispatcher.
/// "No" is the default button, so closing the box or letting Velopack's 30 second hook limit expire keeps the user's data.
/// </summary>
public static class UninstallPrompt
{
    public static bool AskYesNoDefaultNo(string text, string caption)
    {
        var result = PInvoke.MessageBox(
            HWND.Null,
            text,
            caption,
            MESSAGEBOX_STYLE.MB_YESNO | MESSAGEBOX_STYLE.MB_ICONQUESTION | MESSAGEBOX_STYLE.MB_DEFBUTTON2 | MESSAGEBOX_STYLE.MB_SETFOREGROUND);
        return result == MESSAGEBOX_RESULT.IDYES;
    }
}
