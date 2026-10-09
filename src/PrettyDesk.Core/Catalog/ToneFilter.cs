using PrettyDesk.Core.Abstractions;

namespace PrettyDesk.Core.Catalog;

/// <summary>
/// The "only dark wallpapers" rule (general setting <c>DarkWallpapersOnly</c>). Rotation pools, game wallpapers, fixed-wallpaper
/// choices and the picker all apply the same rule through this class so they cannot disagree.
/// </summary>
public static class ToneFilter
{
    /// <summary>True when a wallpaper of <paramref name="tone"/> may be shown. Mid-tone and light wallpapers are excluded when dark-only is on.</summary>
    public static bool Allows(string? tone, bool darkOnly) => !darkOnly || tone == Tones.Dark;

    /// <summary>
    /// The tone of a wallpaper whether or not its file is on disk yet: the local asset when it exists (user images and downloaded
    /// packs), otherwise the catalog entry. Null when neither knows the id.
    /// </summary>
    public static string? ToneOf(CatalogDocument catalog, IContentLibrary library, string wallpaperId) =>
        library.TryGetAsset(wallpaperId)?.Tone ?? catalog.FindWallpaper(wallpaperId)?.Tone;
}
