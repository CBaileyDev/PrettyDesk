using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Settings;

namespace PrettyDesk.Core.Detection;

/// <summary>Whether an exe is already covered (catalog or custom game, enabled or not) or is a launcher/helper.</summary>
public static class KnownExecutables
{
    public static bool IsKnown(CatalogDocument catalog, AppSettings settings, string exeName) =>
        catalog.ExcludeExeNames.Contains(exeName, StringComparer.OrdinalIgnoreCase)
        || catalog.Games.Any(g => g.Detection.ExeNames.Contains(exeName, StringComparer.OrdinalIgnoreCase))
        || settings.CustomGames.Any(c => c.ExeNames.Contains(exeName, StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// Samples the shell's notification state every few seconds and raises a hint when an unknown full-screen app has run for
/// 60 s (FR-DET-8). It only looks at the foreground exe name and the shell state; nothing is stored or sent.
/// </summary>
public sealed class UnknownGameMonitor : IDisposable
{
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

    private readonly IForegroundSource _foreground;
    private readonly Func<UserNotificationState> _query;
    private readonly Func<string, bool> _isKnown;
    private readonly UnknownGameHinter _hinter;
    private readonly ISettingsProvider _settings;
    private readonly TimeProvider _time;
    private ITimer? _timer;

    public UnknownGameMonitor(
        IForegroundSource foreground,
        Func<UserNotificationState> query,
        Func<string, bool> isKnown,
        UnknownGameHinter hinter,
        ISettingsProvider settings,
        TimeProvider time)
    {
        _foreground = foreground;
        _query = query;
        _isKnown = isKnown;
        _hinter = hinter;
        _settings = settings;
        _time = time;
    }

    public event Action<UnknownGameHint>? HintAvailable;

    public void Start() => _timer ??= _time.CreateTimer(_ => Tick(), null, SampleInterval, SampleInterval);

    public void Dispose() => _timer?.Dispose();

    /// <summary>One sample. Public so tests and hosts can drive it deterministically.</summary>
    public void Tick()
    {
        var s = _settings.Current;
        var enabled = s.Detection.Enabled && s.Detection.UnknownGameHints && s.Notifications.UnknownGames;
        if (!enabled)
        {
            _hinter.Observe(_time.GetUtcNow(), UserNotificationState.AcceptsNotifications, null, isKnown: false, enabled: false);
            return;
        }

        var exe = _foreground.Current?.ExeName;
        var hint = _hinter.Observe(_time.GetUtcNow(), _query(), exe, exe is not null && _isKnown(exe), enabled: true);
        if (hint is not null)
        {
            HintAvailable?.Invoke(hint);
        }
    }
}
