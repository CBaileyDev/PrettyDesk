using System.Text.Json;
using System.Text.Json.Serialization;
using PrettyDesk.Core.Rotation;

namespace PrettyDesk.Core.Settings;

/// <summary>One monitor's original wallpaper, copied into the backup folder (FR-RESTORE-1).</summary>
public sealed class BackupMonitorEntry
{
    public string MonitorId { get; set; } = "";
    public string? OriginalPath { get; set; }

    /// <summary>File name inside <c>%LOCALAPPDATA%\PrettyDesk\backup\</c>, or null when no file could be copied.</summary>
    public string? BackupFile { get; set; }
}

public sealed class BackupMetadata
{
    public List<string> SlideshowItems { get; set; } = [];
    public uint SlideshowOptions { get; set; }
    public uint SlideshowInterval { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<BackupMonitorEntry> Monitors { get; set; } = [];

    /// <summary>DWPOS_* value as returned by <c>IDesktopWallpaper.GetPosition</c>.</summary>
    public int Position { get; set; }

    /// <summary>COLORREF background colour (0x00BBGGRR).</summary>
    public uint BackgroundColor { get; set; }

    /// <summary>"picture", "slideshow", "spotlight" or "solid" — what the user had before us (FR-APPLY-7).</summary>
    public string BackgroundType { get; set; } = "picture";
}

/// <summary>Everything that is state rather than preference: lives in <c>state.json</c> (SPEC §5.6).</summary>
public sealed class AppState : IJsonFileValidatable
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Dictionary<string, ContextRotationState> Rotation { get; set; } = [];

    /// <summary>Last path applied per monitor id — lets a restart skip a redundant apply (FR-APPLY-4).</summary>
    public Dictionary<string, string> LastApplied { get; set; } = [];

    public BackupMetadata? Backup { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public bool IsValid() =>
        Rotation.Values.All(r => r is not null && r.Bag.Remaining.All(s => !string.IsNullOrWhiteSpace(s)) &&
            r.Bag.Shown.All(s => !string.IsNullOrWhiteSpace(s)) && r.AdditionalWallpaperIds.All(s => !string.IsNullOrWhiteSpace(s))) &&
        LastApplied.Values.All(s => !string.IsNullOrWhiteSpace(s)) &&
        (Backup is null || Backup.SlideshowItems.All(s => !string.IsNullOrWhiteSpace(s)) &&
            Backup.Monitors.All(m => m is not null && (m.BackupFile is null ||
                m.BackupFile.Length > 0 && m.BackupFile == Path.GetFileName(m.BackupFile) &&
                !m.BackupFile.Contains(':', StringComparison.Ordinal) && !m.BackupFile.Contains('\\', StringComparison.Ordinal))));
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.Unspecified,
    PropertyNameCaseInsensitive = true,
    RespectNullableAnnotations = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AppState))]
public sealed partial class AppStateJsonContext : JsonSerializerContext;
