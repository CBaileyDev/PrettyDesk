using System.Text.Json.Serialization;

namespace PrettyDesk.Core.Catalog;

public static class Tones
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string Mid = "mid";
}

public sealed record FocalPoint(double X, double Y)
{
    public static FocalPoint Center { get; } = new(0.5, 0.5);
}

public sealed record FileRef
{
    public string Path { get; init; } = "";
    public int W { get; init; }
    public int H { get; init; }
    public long Bytes { get; init; }
    public string Sha256 { get; init; } = "";
}

public sealed record DetectionRules
{
    public IReadOnlyList<string> ExeNames { get; init; } = [];
    public IReadOnlyList<uint> SteamAppIds { get; init; } = [];
    public IReadOnlyList<string> PathContains { get; init; } = [];
    public IReadOnlyList<string> WindowTitleContains { get; init; } = [];
    public IReadOnlyList<string> ExcludeExeNames { get; init; } = [];
}

public sealed record GameEntry
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public DetectionRules Detection { get; init; } = new();
    public string PackId { get; init; } = "";
    public string AddedIn { get; init; } = "";

    /// <summary>ISO date the owner confirmed the exe names in the Detection log (docs/GAME_CATALOG_SEED.md).</summary>
    public string? Verified { get; init; }
}

public sealed record WallpaperEntry
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Tone { get; init; } = Tones.Dark;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> SetupMatch { get; init; } = [];
    public FocalPoint Focal { get; init; } = FocalPoint.Center;
    public string? Accent { get; init; }
    public bool Starter { get; init; }
    public string Role { get; init; } = "default";
    public FileRef? Thumb { get; init; }
    public IReadOnlyDictionary<string, FileRef> Variants { get; init; } = new Dictionary<string, FileRef>();
}

public sealed record PackEntry
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "game";
    public int Version { get; init; } = 1;
    public string Title { get; init; } = "";
    public IReadOnlyList<WallpaperEntry> Wallpapers { get; init; } = [];
}

public sealed record CollectionEntry
{
    public string Id { get; init; } = "";
    public string PackId { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<string> SetupMatch { get; init; } = [];
    public string Tone { get; init; } = Tones.Dark;
    public int Order { get; init; }
}

public sealed record CatalogDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string CatalogVersion { get; init; } = "";
    public string MinAppVersion { get; init; } = "1.0.0";
    public string ContentBaseUrl { get; init; } = "";

    /// <summary>Launchers and helpers that must never count as a game (docs/GAME_CATALOG_SEED.md §1).</summary>
    public IReadOnlyList<string> ExcludeExeNames { get; init; } = [];

    public IReadOnlyList<GameEntry> Games { get; init; } = [];
    public IReadOnlyList<PackEntry> Packs { get; init; } = [];
    public IReadOnlyList<CollectionEntry> Collections { get; init; } = [];
    public string Disclaimer { get; init; } = "";

    public static CatalogDocument Empty { get; } = new();

    public PackEntry? FindPack(string packId) => Packs.FirstOrDefault(p => p.Id == packId);

    public GameEntry? FindGame(string gameId) => Games.FirstOrDefault(g => g.Id == gameId);

    public WallpaperEntry? FindWallpaper(string wallpaperId)
    {
        foreach (var pack in Packs)
        {
            foreach (var wallpaper in pack.Wallpapers)
            {
                if (wallpaper.Id == wallpaperId)
                {
                    return wallpaper;
                }
            }
        }

        return null;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.Unspecified,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CatalogDocument))]
public sealed partial class CatalogJsonContext : JsonSerializerContext;
