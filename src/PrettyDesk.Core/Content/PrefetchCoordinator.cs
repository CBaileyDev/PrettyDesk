using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Detection.Discovery;
using PrettyDesk.Core.Orchestration;

namespace PrettyDesk.Core.Content;

/// <summary>
/// Requests the packs that will be needed soon (FR-CON-4): the default collections the user selected (so rotation has its
/// whole pool) and the packs of installed + enabled games (so the wallpaper is ready the moment a game starts). Requests are
/// idempotent, so it simply re-evaluates whenever settings, the catalog or the displays change.
/// </summary>
public sealed class PrefetchCoordinator : IDisposable
{
    private static readonly TimeSpan ScanCacheLifetime = TimeSpan.FromMinutes(10);

    private readonly ISettingsProvider _settings;
    private readonly ICatalogProvider _catalog;
    private readonly IMonitorProvider _monitors;
    private readonly IContentBrowser _content;
    private readonly IInstalledGameScanner _scanner;
    private readonly TimeProvider _time;
    private IReadOnlyList<InstalledGame> _scan = [];
    private DateTimeOffset _scannedAt = DateTimeOffset.MinValue;

    public PrefetchCoordinator(
        ISettingsProvider settings,
        ICatalogProvider catalog,
        IMonitorProvider monitors,
        IContentBrowser content,
        IInstalledGameScanner scanner,
        TimeProvider time)
    {
        _settings = settings;
        _catalog = catalog;
        _monitors = monitors;
        _content = content;
        _scanner = scanner;
        _time = time;
        _settings.Changed += Run;
        _catalog.Changed += Run;
        _monitors.Changed += Run;
    }

    /// <summary>The pack ids that should be on disk right now.</summary>
    public IReadOnlySet<string> WantedPacks()
    {
        var settings = _settings.Current;
        var catalog = _catalog.Current;
        var packs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var collectionId in settings.Default.Selection.Collections)
        {
            if (collectionId == ContentIds.UserPackId)
            {
                continue;
            }

            if (catalog.Collections.FirstOrDefault(c => c.Id == collectionId)?.PackId is { } packId)
            {
                packs.Add(packId);
            }
        }

        foreach (var wallpaperId in settings.Default.Selection.Wallpapers)
        {
            if (catalog.Packs.FirstOrDefault(p => p.Wallpapers.Any(w => w.Id == wallpaperId))?.Id is { } owner)
            {
                packs.Add(owner);
            }
        }

        if (settings.Content.PrefetchInstalledGames)
        {
            foreach (var game in InstalledGameScanner.MatchCatalog(catalog, Scan()))
            {
                if (settings.IsGameEnabled(game.Id) && !string.IsNullOrEmpty(game.PackId))
                {
                    packs.Add(game.PackId);
                }
            }
        }

        return packs;
    }

    public void Run()
    {
        var monitors = _monitors.GetMonitors();
        if (monitors.Count == 0)
        {
            return;
        }

        foreach (var packId in WantedPacks())
        {
            _content.RequestPack(packId, monitors);
        }
    }

    public void Dispose()
    {
        _settings.Changed -= Run;
        _catalog.Changed -= Run;
        _monitors.Changed -= Run;
    }

    private IReadOnlyList<InstalledGame> Scan()
    {
        var now = _time.GetUtcNow();
        if (now - _scannedAt > ScanCacheLifetime)
        {
            _scan = _scanner.Scan();
            _scannedAt = now;
        }

        return _scan;
    }
}
