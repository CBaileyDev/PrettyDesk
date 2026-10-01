using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrettyDesk.Core.Settings;

[JsonConverter(typeof(CamelEnumConverter<WallpaperMode>))]
public enum WallpaperMode
{
    Fixed,
    Rotate,
}

[JsonConverter(typeof(CamelEnumConverter<RotationOrder>))]
public enum RotationOrder
{
    Shuffle,
    Sequential,
}

[JsonConverter(typeof(CamelEnumConverter<MonitorMode>))]
public enum MonitorMode
{
    /// <summary>Same wallpaper on all monitors, each with its own aspect-correct variant.</summary>
    Same,

    /// <summary>Different wallpaper per monitor, no duplicates when the pool allows.</summary>
    Different,
}

/// <summary>Writes enums as camelCase strings (<c>rotate</c>, <c>shuffle</c>) as shown in SPEC §5.7.</summary>
public sealed class CamelEnumConverter<T> : JsonStringEnumConverter<T>
    where T : struct, Enum
{
    public CamelEnumConverter()
        : base(JsonNamingPolicy.CamelCase)
    {
    }
}

/// <summary>Every section keeps unknown fields so a newer/older app never loses data (SPEC §5.7).</summary>
public abstract class SettingsSection
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class GeneralSettings : SettingsSection
{
    public bool StartWithWindows { get; set; } = true;
    public bool RestoreOnExit { get; set; }
    public bool Paused { get; set; }
    public DateTimeOffset? PauseUntil { get; set; }
    public bool OnboardingCompleted { get; set; }
    public bool TrayHintShown { get; set; }

    /// <summary>Follow the "beta" update channel (SPEC §9) instead of "stable".</summary>
    public bool BetaUpdates { get; set; }
}

public sealed class DetectionSettings : SettingsSection
{
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 2;
    public int DetectDelaySeconds { get; set; } = 3;
    public int ExitGraceSeconds { get; set; } = 10;
    public bool UnknownGameHints { get; set; } = true;
}

public sealed class SelectionSettings : SettingsSection
{
    public List<string> Collections { get; set; } = ["default.matte-black"];
    public List<string> Wallpapers { get; set; } = [];
    public List<string> Excluded { get; set; } = [];
}

public sealed class DefaultSettings : SettingsSection
{
    public WallpaperMode Mode { get; set; } = WallpaperMode.Rotate;
    public string? FixedWallpaperId { get; set; }
    public SelectionSettings Selection { get; set; } = new();
    public RotationInterval Interval { get; set; } = RotationInterval.Every(TimeSpan.FromMinutes(30));
    public RotationOrder Order { get; set; } = RotationOrder.Shuffle;
    public bool FollowWindowsTheme { get; set; }
    public bool PauseRotationOnBatterySaver { get; set; } = true;
}

public sealed class GameSettings : SettingsSection
{
    public bool Enabled { get; set; } = true;
    public WallpaperMode Mode { get; set; } = WallpaperMode.Rotate;
    public string? FixedWallpaperId { get; set; }
    public List<string> Excluded { get; set; } = [];
    public RotationInterval Interval { get; set; } = RotationInterval.Session;
    public RotationOrder Order { get; set; } = RotationOrder.Shuffle;
}

public sealed class CustomGame : SettingsSection
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> ExeNames { get; set; } = [];

    /// <summary>Wallpaper ids: <c>user:file.png</c> for the user's own images, or any pack wallpaper id.</summary>
    public List<string> Wallpapers { get; set; } = [];
}

public sealed class MonitorSettings : SettingsSection
{
    public MonitorMode Mode { get; set; } = MonitorMode.Same;
    public bool GameOnSecondaryOnly { get; set; }
}

public sealed class ContentSettings : SettingsSection
{
    public double MaxCacheGB { get; set; } = 3;
    public bool PrefetchInstalledGames { get; set; } = true;
}

public sealed class NotificationSettings : SettingsSection
{
    public bool DownloadErrors { get; set; } = true;
    public bool Updates { get; set; } = true;
    public bool UnknownGames { get; set; } = true;
}

public sealed class AppSettings : SettingsSection
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public GeneralSettings General { get; set; } = new();
    public DetectionSettings Detection { get; set; } = new();
    public DefaultSettings Default { get; set; } = new();
    public Dictionary<string, GameSettings> Games { get; set; } = [];
    public List<CustomGame> CustomGames { get; set; } = [];
    public MonitorSettings Monitors { get; set; } = new();
    public ContentSettings Content { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();

    /// <summary>Settings for a game, creating the documented defaults on first access (enabled, rotate, per-session).</summary>
    public GameSettings GetGame(string gameId)
    {
        if (!Games.TryGetValue(gameId, out var game))
        {
            game = new GameSettings();
            Games[gameId] = game;
        }

        return game;
    }

    public bool IsGameEnabled(string gameId) => !Games.TryGetValue(gameId, out var game) || game.Enabled;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.Unspecified,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
