namespace PrettyDesk.Core.Detection;

/// <summary>Shell notification state as reported by <c>SHQueryUserNotificationState</c> (values verified against Microsoft Learn).</summary>
public enum UserNotificationState
{
    NotPresent = 1,
    Busy = 2,
    RunningD3DFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7,
}

public sealed record UnknownGameHint(string ExeName);

/// <summary>
/// FR-DET-8: offers "Add <em>exe</em> as a game?" once per exe after the foreground app has been in a full-screen /
/// busy state for 60 s or longer while not being a known game, a launcher, or an everyday full-screen app (browsers,
/// video players, presentations). Pure logic driven by observations and a clock.
/// </summary>
public sealed class UnknownGameHinter
{
    public static readonly TimeSpan Threshold = TimeSpan.FromSeconds(60);

    /// <summary>Full-screen apps that are very unlikely to be games.</summary>
    public static IReadOnlySet<string> EverydayApps { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe",
        "vlc.exe", "mpc-hc64.exe", "mpc-hc.exe", "mpv.exe", "potplayer64.exe", "potplayermini64.exe", "wmplayer.exe", "netflix.exe",
        "powerpnt.exe", "winword.exe", "excel.exe", "onenote.exe", "teams.exe", "ms-teams.exe", "zoom.exe", "slack.exe", "discord.exe",
        "code.exe", "devenv.exe", "obs64.exe", "mstsc.exe", "applicationframehost.exe", "searchhost.exe", "shellexperiencehost.exe",
        "lockapp.exe", "logonui.exe", "dwm.exe", "taskmgr.exe", "cmd.exe", "powershell.exe", "windowsterminal.exe", "wt.exe",
    };

    private readonly HashSet<string> _hinted = new(StringComparer.OrdinalIgnoreCase);
    private string? _candidate;
    private DateTimeOffset _since;

    /// <summary>Feeds one observation; returns a hint when the threshold is crossed for a new exe.</summary>
    /// <param name="isKnown">True when the exe is already covered by a catalog or custom game, or is a known launcher.</param>
    public UnknownGameHint? Observe(DateTimeOffset now, UserNotificationState state, string? foregroundExe, bool isKnown, bool enabled)
    {
        var fullScreen = state is UserNotificationState.Busy or UserNotificationState.RunningD3DFullScreen;
        if (!enabled || !fullScreen || string.IsNullOrEmpty(foregroundExe) || isKnown || EverydayApps.Contains(foregroundExe) || _hinted.Contains(foregroundExe))
        {
            _candidate = null;
            return null;
        }

        if (!string.Equals(_candidate, foregroundExe, StringComparison.OrdinalIgnoreCase))
        {
            _candidate = foregroundExe;
            _since = now;
            return null;
        }

        if (now - _since < Threshold)
        {
            return null;
        }

        _hinted.Add(foregroundExe);
        _candidate = null;
        return new UnknownGameHint(foregroundExe);
    }

    /// <summary>Remembers hints from a previous run so each exe is only ever offered once.</summary>
    public void MarkHinted(IEnumerable<string> exes)
    {
        foreach (var exe in exes)
        {
            _hinted.Add(exe);
        }
    }
}
