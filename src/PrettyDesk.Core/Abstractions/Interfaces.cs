using PrettyDesk.Core.Catalog;

namespace PrettyDesk.Core.Abstractions;

/// <summary>Enumerates processes without opening them (Toolhelp snapshot on Windows).</summary>
public interface IProcessSource
{
    IReadOnlyList<ProcessInfo> Snapshot();
}

/// <summary>
/// Lazily fetches expensive facts about a candidate process. Implementations must never request more than
/// PROCESS_QUERY_LIMITED_INFORMATION and must return null (not throw) when access is denied (FR-DET-3).
/// </summary>
public interface IProcessDetailsSource
{
    string? TryGetImagePath(int pid);

    string? TryGetMainWindowTitle(int pid);
}

public interface IForegroundSource
{
    ForegroundInfo? Current { get; }

    event Action? Changed;
}

/// <summary>Steam's <c>RunningAppID</c> value (0 when nothing runs).</summary>
public interface ISteamRunningAppSource
{
    uint CurrentAppId { get; }

    event Action? Changed;
}

public interface IMonitorProvider
{
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Raised (already debounced by the implementation or consumer) on any display change (FR-MON-4).</summary>
    event Action? Changed;
}

public interface IWallpaperSetter
{
    /// <summary>Sets <paramref name="path"/> on a monitor with Fill positioning (FR-APPLY-1).</summary>
    Task SetAsync(string monitorId, string path, CancellationToken cancellationToken = default);

    Task<string?> GetAsync(string monitorId, CancellationToken cancellationToken = default);
}

/// <summary>Facts about the Windows environment that influence applying (FR-WP-8/9, FR-APPLY-7).</summary>
public interface ISystemState
{
    bool IsLightTheme { get; }

    bool IsBatterySaverOn { get; }

    /// <summary>True when policy forbids changing the wallpaper (FR-APPLY-7).</summary>
    bool IsWallpaperPolicyLocked { get; }

    event Action? Changed;
}

public sealed record InstalledGame(string Source, string InstallPath, string? ExeHint, uint? SteamAppId, string DisplayName);

/// <summary>Local, read-only discovery of installed games (FR-DET-9).</summary>
public interface IInstalledGameScanner
{
    IReadOnlyList<InstalledGame> Scan();
}

/// <summary>Gives the orchestrator access to what is on disk and lets it ask for more (FR-CON-4).</summary>
public interface IContentLibrary
{
    /// <summary>All wallpaper ids that belong to a pack according to the catalog (downloaded or not).</summary>
    IReadOnlyList<string> GetWallpaperIds(string packId);

    /// <summary>Returns the asset when at least one variant is on disk, otherwise null.</summary>
    WallpaperAsset? TryGetAsset(string wallpaperId);

    /// <summary>Starts a background download of the pack variants needed by the monitors, if not already running.</summary>
    void RequestPack(string packId, IReadOnlyList<MonitorInfo> monitors);

    event Action<string>? PackChanged;
}

public interface IWallpaperRenderer
{
    /// <summary>Renders (or fetches from cache) an exact-pixel PNG for the monitor and returns its path (FR-APPLY-2/3).</summary>
    Task<string> RenderAsync(WallpaperAsset asset, MonitorInfo monitor, CancellationToken cancellationToken = default);
}
