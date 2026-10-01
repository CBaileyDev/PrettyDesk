using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrettyDesk.App.Services;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Imaging;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Windows;

namespace PrettyDesk.App.Composition;

/// <summary>
/// Starts and stops everything in a defined order. Anything slow or blocking runs off the UI thread so the tray icon appears
/// fast (NFR-5), and every failure is contained: the tray must never crash (NFR-13).
/// </summary>
public sealed partial class AppRuntime : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AppRuntime> _logger;
    private bool _started;

    public AppRuntime(IServiceProvider services)
    {
        _services = services;
        _logger = services.GetRequiredService<ILogger<AppRuntime>>();
    }

    private T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    /// <summary>Phase 1 (UI thread, quick): tray and window plumbing so the user sees PrettyDesk immediately.</summary>
    public void StartUi()
    {
        Get<TrayService>().Start();
        Get<WindowManager>().QuitRequested += OnQuitRequested;

        var settings = Get<SettingsService>();
        var notifications = Get<NotificationCoordinator>();
        if (settings.RecoveredFromCorruption)
        {
            notifications.OnSettingsRecovered();
        }

        Get<UnknownGameMonitor>().HintAvailable += notifications.OnUnknownGame;
    }

    /// <summary>Phase 2 (background): load content, start watchers and the orchestrator.</summary>
    public async Task StartEngineAsync()
    {
        await Task.Run(() =>
        {
            Get<AppPaths>().EnsureCreated();
            var catalog = Get<Core.Catalog.CatalogService>();
            catalog.LoadInitial();
            catalog.StartPeriodicRefresh();

            var appState = Get<AppStateService>();
            _ = appState.Current;

            Get<ForegroundWatcher>().Start();
            Get<SteamRunningAppWatcher>().Start();
            var shell = Get<ShellMessageWindow>();
            shell.TaskbarCreated += () => Get<WallpaperOrchestrator>().NotifyDesktopReset();
            shell.Start();

            var session = Get<SessionEvents>();
            session.Unlocked += () => Get<WallpaperOrchestrator>().NotifyUnlock();
            session.Resumed += () => Get<WallpaperOrchestrator>().Poke();

            var detection = Get<DetectionService>();
            detection.ActiveChanged += change => Get<WallpaperOrchestrator>().SetActiveGame(change.Current);
            _ = Get<DetectionConfigurator>();
            detection.Start();

            Get<WallpaperOrchestrator>().Start();
            Get<PrefetchCoordinator>().Run();
            Get<UnknownGameMonitor>().Start();

            // Keep the Run-key entry pointing at this exe (portable copies move, installs update in place).
            var startup = new StartupRegistration();
            if (Get<ISettingsProvider>().Current.General.StartWithWindows && Environment.ProcessPath is { } exe && (!startup.IsEnabled || startup.IsStale(exe)))
            {
                startup.Enable(exe);
            }
        });
        _started = true;
        LogStarted();
    }

    /// <summary>Quit: optionally restore the user's original wallpaper (FR-RESTORE-3), then release everything.</summary>
    public async Task StopAsync()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        try
        {
            // Stop applying first so nothing overwrites the restored wallpaper.
            await Get<WallpaperOrchestrator>().DisposeAsync();
            if (Get<ISettingsProvider>().Current.General.RestoreOnExit)
            {
                await Get<WallpaperBackupService>().RestoreAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
#pragma warning disable CA1031 // Shutting down: a restore failure is logged and must not block exit.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogStopFailed(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Get<AppStateService>().Dispose();
        await Get<DetectionService>().DisposeAsync();
        Get<UnknownGameMonitor>().Dispose();
        Get<PrefetchCoordinator>().Dispose();
        Get<ForegroundWatcher>().Dispose();
        Get<SteamRunningAppWatcher>().Dispose();
        Get<ShellMessageWindow>().Dispose();
        Get<SessionEvents>().Dispose();
        Get<WindowsSystemState>().Dispose();
        Get<DesktopWallpaperService>().Dispose();
        Get<TrayService>().Dispose();
        Get<NotificationCoordinator>().Dispose();
        Get<Core.Catalog.CatalogService>().Dispose();
        Get<Core.Content.ContentLibrary>().Dispose();
        Get<WallpaperRenderer>().Dispose();
    }

    /// <summary>Raised on the UI thread after the user chose Quit; <see cref="App"/> shuts the process down.</summary>
    public event Action? QuitRequested;

    private void OnQuitRequested() => QuitRequested?.Invoke();

    [LoggerMessage(Level = LogLevel.Information, Message = "PrettyDesk started")]
    private partial void LogStarted();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shutdown cleanup failed")]
    private partial void LogStopFailed(Exception ex);
}
