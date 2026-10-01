using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrettyDesk.App.Composition;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Windows;
using Serilog;

namespace PrettyDesk.App.Services;

/// <summary>
/// Velopack uninstall hook (SPEC §9, FR-RESTORE-2): put the original wallpaper back, remove our Run entry, and ask before deleting
/// the user's data. Runs in a bare process with a hard 30 second limit, so every step is best effort and logged, never thrown.
/// The "start with Windows" entry is created by first-run onboarding (which asks), not here (docs/adr/0006).
/// </summary>
public static class InstallHooks
{
    public static void BeforeUninstall()
    {
        var paths = new AppPaths();

        Attempt("remove the Run key", () => new StartupRegistration().Disable());
        Attempt("restore the original wallpaper", () => RestoreWallpaper(paths));
        Attempt("ask about user data", () =>
        {
            if (UninstallPrompt.AskYesNoDefaultNo(Strings.Uninstall_KeepDataBody, Strings.Uninstall_KeepDataTitle))
            {
                DeleteData(paths);
            }
        });
    }

    private static void RestoreWallpaper(AppPaths paths)
    {
        using var provider = new ServiceCollection().AddPrettyDesk(paths).BuildServiceProvider();
        var backup = provider.GetRequiredService<WallpaperBackupService>();
        var result = backup.RestoreAsync().GetAwaiter().GetResult();
        Log.Information("Uninstall restore: {Monitors} monitor(s) restored, nothing to restore: {Nothing}", result.MonitorsRestored, result.NothingToRestore);
        provider.GetRequiredService<DesktopWallpaperService>().Dispose();
    }

    private static void DeleteData(AppPaths paths)
    {
        // Our own log file is still open in this process, so the logs folder may survive; everything else goes.
        foreach (var directory in new[] { paths.Packs, paths.RenderCache, paths.UserImages, paths.Backup, paths.CatalogDirectory })
        {
            TryDelete(() => Directory.Delete(directory, recursive: true));
        }

        foreach (var file in new[] { paths.SettingsFile, paths.StateFile })
        {
            TryDelete(() => File.Delete(file));
        }
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not delete part of the user data during uninstall");
        }
    }

    private static void Attempt(string what, Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // Uninstall must continue past any single failed step.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Warning(ex, "Uninstall step failed: {Step}", what);
        }
    }
}
