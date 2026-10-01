namespace PrettyDesk.Core.Catalog;

/// <summary>A variant file that exists on disk.</summary>
public sealed record LocalVariant(string Key, string Path, int Width, int Height);

/// <summary>A wallpaper with at least one local variant: everything the renderer needs.</summary>
public sealed record WallpaperAsset(
    string Id,
    string PackId,
    string Title,
    string Tone,
    FocalPoint Focal,
    IReadOnlyDictionary<string, LocalVariant> Variants,
    string ContentHash,
    bool IsUserImage = false);
