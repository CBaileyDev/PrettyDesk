namespace PrettyDesk.Core.Orchestration;

public enum OrchestratorMode
{
    /// <summary>The user's default wallpaper / rotation is showing.</summary>
    Default,

    /// <summary>A game is running and its wallpaper is showing.</summary>
    Game,

    /// <summary>The game exited; its wallpaper stays for the exit-grace period (FR-DET-5).</summary>
    GameGrace,

    /// <summary>A game is running but its wallpapers are still downloading: the current wallpaper stays ("Getting wallpapers…").</summary>
    GameLoading,

    /// <summary>Paused by the user: the desktop is frozen (FR-WP-7).</summary>
    Paused,

    /// <summary>Applying is impossible, e.g. wallpaper locked by policy (FR-APPLY-7).</summary>
    Blocked,

    /// <summary>A temporary preview is on the desktop (FR-WP-10).</summary>
    Preview,
}

/// <summary>
/// Structured status for the tray and Home page. Strings are composed in the UI layer from these fields so they stay
/// localisable (NFR-11).
/// </summary>
public sealed record OrchestratorStatus
{
    public OrchestratorMode Mode { get; init; } = OrchestratorMode.Default;
    public string? GameId { get; init; }
    public string? GameName { get; init; }

    /// <summary>Title of the default selection, e.g. "Matte Black".</summary>
    public string? DefaultTitle { get; init; }

    public bool IsRotating { get; init; }
    public DateTimeOffset? NextChange { get; init; }
    public DateTimeOffset? PausedUntil { get; init; }

    /// <summary>Monitor id → wallpaper id currently showing (or being kept).</summary>
    public IReadOnlyDictionary<string, string> WallpaperByMonitor { get; init; } = new Dictionary<string, string>();

    /// <summary>Position of the current wallpaper within its pool, for "wallpaper 2 of 4".</summary>
    public int PoolIndex { get; init; }

    public int PoolSize { get; init; }

    /// <summary>True when applying failed and a retry is scheduled; the UI shows a human message (never a stack trace).</summary>
    public bool ApplyFailed { get; init; }

    public bool NoContent { get; init; }

    public bool SameAs(OrchestratorStatus? other) =>
        other is not null
        && Mode == other.Mode
        && GameId == other.GameId
        && GameName == other.GameName
        && DefaultTitle == other.DefaultTitle
        && IsRotating == other.IsRotating
        && NextChange == other.NextChange
        && PausedUntil == other.PausedUntil
        && PoolIndex == other.PoolIndex
        && PoolSize == other.PoolSize
        && ApplyFailed == other.ApplyFailed
        && NoContent == other.NoContent
        && WallpaperByMonitor.Count == other.WallpaperByMonitor.Count
        && WallpaperByMonitor.All(kv => other.WallpaperByMonitor.TryGetValue(kv.Key, out var v) && v == kv.Value);
}
