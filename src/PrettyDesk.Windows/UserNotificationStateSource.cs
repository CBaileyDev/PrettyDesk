using PrettyDesk.Core.Detection;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace PrettyDesk.Windows;

/// <summary>Wraps <c>SHQueryUserNotificationState</c> for the unknown-game hint (FR-DET-8).</summary>
public sealed class UserNotificationStateSource
{
    public static UserNotificationState Query()
    {
        QUERY_USER_NOTIFICATION_STATE state;
        var hr = PInvoke.SHQueryUserNotificationState(out state);
        return hr.Failed || !Enum.IsDefined((UserNotificationState)(int)state) ? UserNotificationState.AcceptsNotifications : (UserNotificationState)(int)state;
    }
}
