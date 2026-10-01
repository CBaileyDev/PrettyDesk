using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Settings;

namespace PrettyDesk.Core.Orchestration;

/// <summary>The "What does your setup look like?" answers from onboarding (SPEC §2, ART_DIRECTION §3).</summary>
public enum SetupStyle
{
    MatteBlack,
    CleanWhite,
    WarmWoodAndPlants,
    Pastel,
    Rgb,
    SurpriseMe,
}

/// <summary>Maps a setup style to the default selection: collections, with tone restrictions where the quiz table asks for them.</summary>
public static class SetupStyles
{
    private sealed record Entry(string CollectionId, string? OnlyTone);

    private static readonly Dictionary<SetupStyle, Entry[]> Map = new()
    {
        [SetupStyle.MatteBlack] =
        [
            new("default.matte-black", null),
            new("default.deep-space", null),
            new("default.steel-blue-night", null),
            new("default.architectural", Tones.Dark),
        ],
        [SetupStyle.CleanWhite] =
        [
            new("default.clean-white", null),
            new("default.soft-gradients", Tones.Light),
            new("default.architectural", Tones.Light),
            new("default.misty-nature", Tones.Light),
        ],
        [SetupStyle.WarmWoodAndPlants] =
        [
            new("default.warm-minimal", null),
            new("default.sage-botanical", null),
            new("default.painted", null),
            new("default.cozy-lofi", null),
        ],
        [SetupStyle.Pastel] =
        [
            new("default.pastel", null),
            new("default.soft-gradients", null),
            new("default.cozy-lofi", null),
        ],
        [SetupStyle.Rgb] =
        [
            new("default.neon-minimal", null),
            new("default.deep-space", null),
            new("default.steel-blue-night", null),
        ],
    };

    /// <summary>The collection ids a style selects (empty for "Surprise me", which picks starter wallpapers instead).</summary>
    public static IReadOnlyList<string> CollectionsFor(SetupStyle style) =>
        Map.TryGetValue(style, out var entries) ? entries.Select(e => e.CollectionId).Distinct().ToList() : [];

    /// <summary>
    /// Applies a style to the default selection. Tone-restricted collections (e.g. "architectural (dark only)") exclude their
    /// wallpapers of the opposite tone; collections missing from the catalog are skipped so a partial catalog still works.
    /// </summary>
    public static void Apply(SetupStyle style, CatalogDocument catalog, SelectionSettings selection)
    {
        selection.Collections = [];
        selection.Wallpapers = [];
        selection.Excluded = [];

        if (style == SetupStyle.SurpriseMe)
        {
            // One hand-picked "best of" from every collection: wallpapers tagged starter.
            foreach (var collection in catalog.Collections.OrderBy(c => c.Order))
            {
                var pack = catalog.FindPack(collection.PackId);
                selection.Wallpapers.AddRange(pack?.Wallpapers.Where(w => w.Starter).Select(w => w.Id) ?? []);
            }

            return;
        }

        if (!Map.TryGetValue(style, out var entries))
        {
            return;
        }

        foreach (var entry in entries)
        {
            var collection = catalog.Collections.FirstOrDefault(c => c.Id == entry.CollectionId);
            if (collection is null)
            {
                continue;
            }

            selection.Collections.Add(collection.Id);
            if (entry.OnlyTone is { } only)
            {
                var opposite = only == Tones.Dark ? Tones.Light : Tones.Dark;
                var pack = catalog.FindPack(collection.PackId);
                selection.Excluded.AddRange(pack?.Wallpapers.Where(w => w.Tone == opposite).Select(w => w.Id) ?? []);
            }
        }

        selection.Collections = selection.Collections.Distinct(StringComparer.Ordinal).ToList();
        selection.Excluded = selection.Excluded.Distinct(StringComparer.Ordinal).ToList();
    }
}
