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
    public string Path { get; set; } = "";
    public int W { get; set; }
    public int H { get; set; }
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed record DetectionRules
{
    public IReadOnlyList<string> ExeNames { get; set; } = [];
    public IReadOnlyList<uint> SteamAppIds { get; set; } = [];
    public IReadOnlyList<string> PathContains { get; set; } = [];
    public IReadOnlyList<string> WindowTitleContains { get; set; } = [];
    public IReadOnlyList<string> ExcludeExeNames { get; set; } = [];
}

public sealed record GameEntry
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DetectionRules Detection { get; set; } = new();
    public string PackId { get; set; } = "";
    public string AddedIn { get; set; } = "";

    /// <summary>ISO date the owner confirmed the exe names in the Detection log (docs/GAME_CATALOG_SEED.md).</summary>
    public string? Verified { get; set; }
}

public sealed record WallpaperEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Tone { get; set; } = Tones.Dark;
    public IReadOnlyList<string> Tags { get; set; } = [];
    public IReadOnlyList<string> SetupMatch { get; set; } = [];
    public FocalPoint Focal { get; set; } = FocalPoint.Center;
    public string? Accent { get; set; }
    public bool Starter { get; set; }
    public string Role { get; set; } = "default";
    public FileRef? Thumb { get; set; }
    public IReadOnlyDictionary<string, FileRef> Variants { get; set; } = new Dictionary<string, FileRef>();
}

public sealed record PackEntry
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "game";
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public IReadOnlyList<WallpaperEntry> Wallpapers { get; set; } = [];
}

public sealed record CollectionEntry
{
    public string Id { get; set; } = "";
    public string PackId { get; set; } = "";
    public string Title { get; set; } = "";
    public IReadOnlyList<string> SetupMatch { get; set; } = [];
    public string Tone { get; set; } = Tones.Dark;
    public int Order { get; set; }
}

public sealed record CatalogDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string CatalogVersion { get; set; } = "";
    public string MinAppVersion { get; set; } = "1.0.0";
    public string ContentBaseUrl { get; set; } = "";

    /// <summary>Launchers and helpers that must never count as a game (docs/GAME_CATALOG_SEED.md §1).</summary>
    public IReadOnlyList<string> ExcludeExeNames { get; set; } = [];

    public IReadOnlyList<GameEntry> Games { get; set; } = [];
    public IReadOnlyList<PackEntry> Packs { get; set; } = [];
    public IReadOnlyList<CollectionEntry> Collections { get; set; } = [];
    public string Disclaimer { get; set; } = "";

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
    RespectNullableAnnotations = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CatalogDocument))]
public sealed partial class CatalogJsonContext : JsonSerializerContext;
