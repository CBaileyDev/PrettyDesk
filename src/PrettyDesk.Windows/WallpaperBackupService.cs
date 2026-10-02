using Microsoft.Extensions.Logging;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Settings;

namespace PrettyDesk.Windows;

/// <param name="UsedBackupCopies">True when a wallpaper now points into <c>backup</c>, so that folder must outlive an uninstall data wipe.</param>
public sealed record RestoreResult(int MonitorsRestored, bool NothingToRestore, string? Note, bool UsedBackupCopies = false);

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
            if (_state.LastSaveFailed && !_state.SaveNow())
            {
                throw new IOException("PrettyDesk couldn't save your original wallpaper backup. Free some disk space or check folder access before retrying.");
            }

            return;
        }

        var monitors = _wallpaper.GetMonitors();
        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("No monitors are available to back up.");
        }

        Directory.CreateDirectory(_backupDirectory);
        if (WindowsSystemState.ReadBackgroundType() == "spotlight")
        {
            // Windows exposes no supported API to resume desktop Spotlight. Preserve the original
            // behavior rather than replacing it with a picture which cannot be restored exactly.
            throw new InvalidOperationException("Windows Spotlight is active. Switch Background to Picture or Slideshow in Windows Settings before enabling PrettyDesk; your Spotlight background has been left unchanged.");
        }
        var backup = new BackupMetadata
        {
            CreatedAt = _time.GetUtcNow(),
            Position = (int)await _wallpaper.GetPositionAsync(cancellationToken),
            BackgroundColor = await _wallpaper.GetBackgroundColorAsync(cancellationToken),
            BackgroundType = WindowsSystemState.ReadBackgroundType(),
        };

        if (backup.BackgroundType == "slideshow")
        {
            // Do not apply anything until the source and timing have been captured successfully.
            var slideshow = await _wallpaper.GetSlideshowAsync(cancellationToken);
            backup.SlideshowItems = slideshow.Items.ToList();
            backup.SlideshowOptions = slideshow.Options;
            backup.SlideshowInterval = slideshow.Interval;
        }

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
        if (!_state.SaveNow())
        {
            throw new IOException("PrettyDesk couldn't save your original wallpaper backup. Free some disk space or check folder access before retrying.");
        }
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
        if (current.Count == 0 && backup.Monitors.Count > 0)
        {
            // GetMonitors folds a COM failure or timeout into an empty list. That is not "nothing to restore": report it so the
            // user (or the uninstall log) knows the original was NOT put back.
            throw new InvalidOperationException("Could not read the monitor list, so the original wallpaper was not restored.");
        }

        if (backup.BackgroundType == "solid")
        {
            await _wallpaper.SetBackgroundColorAsync(backup.BackgroundColor, cancellationToken);
            await _wallpaper.SetEnabledAsync(false, cancellationToken);
            return new RestoreResult(current.Count, NothingToRestore: false, null);
        }

        if (backup.BackgroundType == "slideshow" && backup.SlideshowItems.Count > 0)
        {
            await _wallpaper.SetPositionAsync((DesktopPosition)backup.Position, cancellationToken);
            await _wallpaper.SetBackgroundColorAsync(backup.BackgroundColor, cancellationToken);
            await _wallpaper.SetSlideshowAsync(new SlideshowSnapshot(backup.SlideshowItems, backup.SlideshowOptions, backup.SlideshowInterval), cancellationToken);
            return new RestoreResult(current.Count, NothingToRestore: false, null);
        }
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
            ? $"Your desktop was using {backup.BackgroundType} before PrettyDesk. Automatic restoration is unavailable for this saved backup, so your last picture was restored. Re-enable it in Settings → Personalization → Background."
            : null;
        var usedCopies = false;
        foreach (var monitor in current)
        {
            var path = (usable.FirstOrDefault(x => x.Entry.MonitorId == monitor.Id).Path ?? usable[0].Path)!;
            usedCopies |= path.StartsWith(_backupDirectory, StringComparison.OrdinalIgnoreCase);
        }

        return new RestoreResult(restored, NothingToRestore: false, note, usedCopies);
    }

    /// <summary>
    /// Prefers the user's own original file (it lives outside our data folder, so deleting our data at uninstall cannot blank
    /// the desktop) and falls back to the backup copy when the original has since been moved or deleted.
    /// </summary>
    private string? ResolveFile(BackupMonitorEntry entry)
    {
        if (entry.OriginalPath is not null && File.Exists(entry.OriginalPath))
        {
            return entry.OriginalPath;
        }

        if (entry.BackupFile is not null)
        {
            var copy = Path.Combine(_backupDirectory, entry.BackupFile);
            if (File.Exists(copy))
            {
                return copy;
            }
        }

        return null;
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
