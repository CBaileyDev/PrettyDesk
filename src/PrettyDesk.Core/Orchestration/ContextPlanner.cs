using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Rotation;
using PrettyDesk.Core.Settings;

namespace PrettyDesk.Core.Orchestration;

public static class ContentIds
{
    /// <summary>The pseudo-collection / pack holding the user's own images (FR-CON-7).</summary>
    public const string UserPackId = "user";
}

/// <summary>
/// What a context should show: its candidate pool (only wallpapers present on disk) and how it rotates.
/// <see cref="PendingDownloads"/> counts catalog wallpapers the user could see but that are not on disk yet; zero with an empty
/// pool means there is nothing to wait for (an empty or fully excluded pack), so the caller must not sit in "loading" forever.
/// </summary>
public sealed record ContextPlan(
    string ContextKey,
    IReadOnlyList<string> Pool,
    RotationPolicy Policy,
    bool IsFixed,
    string Title,
    int PendingDownloads = 0);

/// <summary>Pure pool/policy resolution for the default context and game contexts (FR-WP-1/2/8/9).</summary>
public static class ContextPlanner
{
    public const string DefaultKey = "default";

    public static string GameKey(string gameId) => "game:" + gameId;

    public static ContextPlan PlanDefault(AppSettings settings, CatalogDocument catalog, IContentLibrary content, ISystemState system)
    {
        var d = settings.Default;
        var darkOnly = settings.General.DarkWallpapersOnly;
        var title = DescribeSelection(d.Selection, catalog);

        if (d.Mode == WallpaperMode.Fixed && d.FixedWallpaperId is { } fixedId && Eligible(content, fixedId, darkOnly) is not null)
        {
            return new ContextPlan(DefaultKey, [fixedId], new RotationPolicy(d.Order, RotationInterval.Never), IsFixed: true, title);
        }

        var ids = new List<string>();
        foreach (var collectionId in d.Selection.Collections)
        {
            var packId = catalog.Collections.FirstOrDefault(c => c.Id == collectionId)?.PackId
                ?? (collectionId == ContentIds.UserPackId ? ContentIds.UserPackId : collectionId);
            ids.AddRange(content.GetWallpaperIds(packId));
        }

        ids.AddRange(d.Selection.Wallpapers);
        var excluded = d.Selection.Excluded.ToHashSet(StringComparer.Ordinal);
        var available = ids
            .Distinct(StringComparer.Ordinal)
            .Where(id => !excluded.Contains(id))
            .Select(id => Eligible(content, id, darkOnly))
            .Where(a => a is not null)
            .Select(a => a!)
            .ToList();

        if (d.FollowWindowsTheme)
        {
            var wanted = system.IsLightTheme ? Tones.Light : Tones.Dark;
            var matching = available.Where(a => a.Tone == wanted || a.Tone == Tones.Mid).ToList();

            // Never leave the desktop with nothing to show just because the pool has no wallpaper of the right tone.
            if (matching.Count > 0)
            {
                available = matching;
            }
        }

        var policy = new RotationPolicy(d.Order, d.Interval, AutoRotationPaused: d.PauseRotationOnBatterySaver && system.IsBatterySaverOn);
        return new ContextPlan(DefaultKey, available.Select(a => a.Id).ToList(), policy, IsFixed: false, title);
    }

    public static ContextPlan PlanGame(GameDefinition game, AppSettings settings, CatalogDocument catalog, IContentLibrary content)
    {
        var g = settings.GetGame(game.Id);
        var darkOnly = settings.General.DarkWallpapersOnly;
        var excluded = g.Excluded.ToHashSet(StringComparer.Ordinal);
        var custom = settings.CustomGames.FirstOrDefault(c => c.Id == game.Id);

        IEnumerable<string> ids = custom is not null
            ? custom.Wallpapers
            : game.PackId is { } packId ? content.GetWallpaperIds(packId) : [];

        var candidates = ids.Distinct(StringComparer.Ordinal).Where(id => !excluded.Contains(id)).ToList();
        var pool = candidates.Where(id => Eligible(content, id, darkOnly) is not null).ToList();

        // Only catalog packs can still arrive; a custom game's images are local files, so a missing one never loads later.
        // A catalog wallpaper that is hidden by the tone filter is not waiting for anything, so it is not counted.
        var pending = custom is null
            ? candidates.Count(id => content.TryGetAsset(id) is null && ToneFilter.Allows(ToneOfCatalogEntry(catalog, id), darkOnly))
            : 0;

        var key = GameKey(game.Id);
        if (g.Mode == WallpaperMode.Fixed && g.FixedWallpaperId is { } fixedId && Eligible(content, fixedId, darkOnly) is not null)
        {
            return new ContextPlan(key, [fixedId], new RotationPolicy(g.Order, RotationInterval.Never), IsFixed: true, game.DisplayName);
        }

        return new ContextPlan(key, pool, new RotationPolicy(g.Order, g.Interval), IsFixed: false, game.DisplayName, pending);
    }

    /// <summary>A wallpaper's local asset when it exists and the tone filter allows it; otherwise null.</summary>
    private static WallpaperAsset? Eligible(IContentLibrary content, string wallpaperId, bool darkOnly)
    {
        var asset = content.TryGetAsset(wallpaperId);
        return asset is not null && ToneFilter.Allows(asset.Tone, darkOnly) ? asset : null;
    }

    private static string? ToneOfCatalogEntry(CatalogDocument catalog, string wallpaperId) => catalog.FindWallpaper(wallpaperId)?.Tone;

    private static string DescribeSelection(SelectionSettings selection, CatalogDocument catalog)
    {
        if (selection.Collections.Count == 1)
        {
            var id = selection.Collections[0];
            var title = catalog.Collections.FirstOrDefault(c => c.Id == id)?.Title;
            if (title is not null)
            {
                return title;
            }

            if (id == ContentIds.UserPackId)
            {
                return "My images";
            }
        }

        return selection.Collections.Count + selection.Wallpapers.Count > 1 ? "Mixed selection" : "Custom selection";
    }
}
