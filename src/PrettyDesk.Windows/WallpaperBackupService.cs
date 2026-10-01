using Microsoft.Extensions.Logging;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Settings;

namespace PrettyDesk.Windows;

public sealed record RestoreResult(int MonitorsRestored, bool NothingToRestore, string? Note);

/// <summary>
/// Snapshots the user's original wallpaper(s) before PrettyDesk's very first apply and restores them on request or at
/// uninstall (FR-RESTORE-1/2). The files are <em>copied</em> into <c>backup\</c>, so restore works even when the original
/// source file has since been deleted.
/// </summary>
public sealed partial class WallpaperBackupService : IWallpaperBackup
{
    private readonly DesktopWallpaperService _wallpaper;
    private readonly AppStateService _state;
    private readonly string _backupDirectory;
    private readonly string _ownCacheDirectory;
    private readonly TimeProvider _time;
    private readonly ILogger<WallpaperBackupService> _logger;

    public WallpaperBackupService(
        DesktopWallpaperService wallpaper,
        AppStateService state,
        string backupDirectory,
        string ownCacheDirectory,
        TimeProvider time,
        ILogger<WallpaperBackupService> logger)
    {
        _wallpaper = wallpaper;
        _state = state;
        _backupDirectory = backupDirectory;
        _ownCacheDirectory = ownCacheDirectory;
        _time = time;
        _logger = logger;
    }

    public static string TranscodedWallpaperPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");

    public async Task EnsureBackupAsync(CancellationToken cancellationToken = default)
    {
        if (_state.Current.Backup is not null)
        {
            return;
        }

        var monitors = _wallpaper.GetMonitors();
        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("No monitors are available to back up.");
        }

        Directory.CreateDirectory(_backupDirectory);
        var backup = new BackupMetadata
        {
            CreatedAt = _time.GetUtcNow(),
            Position = (int)await _wallpaper.GetPositionAsync(cancellationToken),
            BackgroundColor = await _wallpaper.GetBackgroundColorAsync(cancellationToken),
            BackgroundType = WindowsSystemState.ReadBackgroundType(),
        };

        for (var i = 0; i < monitors.Count; i++)
        {
            var original = await _wallpaper.GetAsync(monitors[i].Id, cancellationToken);
            backup.Monitors.Add(new BackupMonitorEntry
            {
                MonitorId = monitors[i].Id,
                OriginalPath = IsOwnFile(original) ? null : original,
                BackupFile = IsOwnFile(original) ? null : CopyOriginal(i, original),
            });
        }

        _state.Current.Backup = backup;
        _state.SaveNow();
        LogBackedUp(backup.Monitors.Count, backup.BackgroundType);
    }

    /// <summary>Puts back the snapshot. Falls back to the original path, then to nothing, for each monitor.</summary>
    public async Task<RestoreResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var backup = _state.Current.Backup;
        if (backup is null)
        {
            return new RestoreResult(0, NothingToRestore: true, null);
        }

        var current = _wallpaper.GetMonitors();
        var usable = backup.Monitors
            .Select(entry => (Entry: entry, Path: ResolveFile(entry)))
            .Where(x => x.Path is not null)
            .ToList();
        if (usable.Count == 0 || current.Count == 0)
        {
            return new RestoreResult(0, NothingToRestore: true, null);
        }

        var restored = 0;
        foreach (var monitor in current)
        {
            // Match by monitor id; a monitor that did not exist back then gets the first original wallpaper.
            var match = usable.FirstOrDefault(x => x.Entry.MonitorId == monitor.Id);
            var path = match.Path ?? usable[0].Path!;
            await _wallpaper.SetAsync(monitor.Id, path, (DesktopPosition)backup.Position, cancellationToken);
            restored++;
        }

        await _wallpaper.SetBackgroundColorAsync(backup.BackgroundColor, cancellationToken);

        var note = backup.BackgroundType is "slideshow" or "spotlight"
            ? $"Your desktop was using {backup.BackgroundType} before PrettyDesk. Windows does not allow apps to turn that back on, so your last picture was restored. You can re-enable it in Settings → Personalization → Background."
            : null;
        return new RestoreResult(restored, NothingToRestore: false, note);
    }

    private string? ResolveFile(BackupMonitorEntry entry)
    {
        if (entry.BackupFile is not null)
        {
            var copy = Path.Combine(_backupDirectory, entry.BackupFile);
            if (File.Exists(copy))
            {
                return copy;
            }
        }

        return entry.OriginalPath is not null && File.Exists(entry.OriginalPath) ? entry.OriginalPath : null;
    }

    private string? CopyOriginal(int index, string? original)
    {
        try
        {
            // The shell sometimes reports an empty/missing path while the picture lives in the transcoded cache (FR-RESTORE-1).
            var source = !string.IsNullOrEmpty(original) && File.Exists(original) ? original : File.Exists(TranscodedWallpaperPath) ? TranscodedWallpaperPath : null;
            if (source is null)
            {
                return null;
            }

            var extension = Path.GetExtension(source);
            var name = $"monitor{index}_original{(extension.Length == 0 ? ".img" : extension)}";
            File.Copy(source, Path.Combine(_backupDirectory, name), overwrite: true);
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCopyFailed(ex);
            return null;
        }
    }

    private bool IsOwnFile(string? path) =>
        !string.IsNullOrEmpty(path) && path.StartsWith(_ownCacheDirectory, StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backed up the original wallpaper for {Monitors} monitor(s) (background type: {Type})")]
    private partial void LogBackedUp(int monitors, string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not copy the original wallpaper file")]
    private partial void LogCopyFailed(Exception ex);
}
