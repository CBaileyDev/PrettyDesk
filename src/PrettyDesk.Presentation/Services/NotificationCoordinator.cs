using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Detection;
using PrettyDesk.Presentation.Resources;

namespace PrettyDesk.Presentation.Services;

/// <summary>
/// Decides which tray notifications may appear (FR-APP-5): download failures, unknown-game hints, updates and the one-time
/// "settings were reset" notice. Each type is opt-out in settings, and the wallpaper changing is never announced.
/// </summary>
public sealed class NotificationCoordinator : IDisposable
{
    private readonly ISettingsProvider _settings;
    private readonly ICatalogProvider _catalog;
    private readonly IContentBrowser _content;
    private readonly IUpdateService _updates;
    private readonly INotifier _notifier;
    private readonly IUiDispatcher _ui;
    private readonly Action<string> _offerAddGame;
    private readonly HashSet<string> _notifiedPacks = new(StringComparer.Ordinal);
    private string? _notifiedUpdate;

    public NotificationCoordinator(
        ISettingsProvider settings,
        ICatalogProvider catalog,
        IContentBrowser content,
        IUpdateService updates,
        INotifier notifier,
        IUiDispatcher ui,
        Action<string> offerAddGame)
    {
        _settings = settings;
        _catalog = catalog;
        _content = content;
        _updates = updates;
        _notifier = notifier;
        _ui = ui;
        _offerAddGame = offerAddGame;
        _content.DownloadFailed += OnDownloadFailed;
        _updates.Changed += OnUpdateChanged;
    }

    /// <summary>Called when the unknown-game monitor has a hint (FR-DET-8).</summary>
    public void OnUnknownGame(UnknownGameHint hint)
    {
        var s = _settings.Current;
        if (!s.Detection.UnknownGameHints || !s.Notifications.UnknownGames)
        {
            return;
        }

        _ui.Post(() => _notifier.Show(
            Strings.Notify_UnknownGameTitle,
            Strings.Format(Strings.Notify_UnknownGameBody, hint.ExeName),
            () => _offerAddGame(hint.ExeName)));
    }

    /// <summary>Tells the user once that a corrupt settings file was replaced (SPEC §5.7).</summary>
    public void OnSettingsRecovered() => _ui.Post(() => _notifier.Show(Strings.Notify_SettingsRecoveredTitle, Strings.Notify_SettingsRecoveredBody));

    public void Dispose()
    {
        _content.DownloadFailed -= OnDownloadFailed;
        _updates.Changed -= OnUpdateChanged;
    }

    private void OnDownloadFailed(string packId, Exception error)
    {
        _ = error;
        if (!_settings.Current.Notifications.DownloadErrors || !_notifiedPacks.Add(packId))
        {
            return;
        }

        // One notification per pack per run: a flaky connection must not spam the user.
        var name = _catalog.Current.Games.FirstOrDefault(g => g.PackId == packId)?.DisplayName ?? _catalog.Current.FindPack(packId)?.Title ?? packId;
        _ui.Post(() => _notifier.Show(Strings.Notify_DownloadFailedTitle, Strings.Format(Strings.Notify_DownloadFailedBody, name)));
    }

    private void OnUpdateChanged()
    {
        var state = _updates.State;
        if (state.Kind is not (UpdateStateKind.Available or UpdateStateKind.ReadyToInstall) || state.Version is null || state.Version == _notifiedUpdate || !_settings.Current.Notifications.Updates)
        {
            return;
        }

        _notifiedUpdate = state.Version;
        _ui.Post(() => _notifier.Show(Strings.Notify_UpdateTitle, Strings.Format(Strings.Notify_UpdateBody, state.Version)));
    }
}
