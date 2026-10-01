using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PrettyDesk.App.Services;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Detection.Discovery;
using PrettyDesk.Core.Imaging;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Core.Rotation;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Presentation.ViewModels;
using PrettyDesk.Windows;
using Serilog;

namespace PrettyDesk.App.Composition;

/// <summary>The dependency graph (SPEC §5.1: Microsoft.Extensions.DependencyInjection). Singletons for services, factories for pages.</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddPrettyDesk(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));

        AddSettings(services, paths);
        AddContent(services, paths);
        AddWindows(services, paths);
        AddRuntime(services);
        AddUi(services);
        return services;
    }

    private static void AddSettings(IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(sp => new SettingsService(new JsonFileStore<AppSettings>(
            paths.SettingsFile, SettingsJsonContext.Default.AppSettings, AppSettings.CurrentSchemaVersion, sp.GetRequiredService<TimeProvider>())));
        services.AddSingleton<ISettingsProvider>(sp => sp.GetRequiredService<SettingsService>());
        services.AddSingleton(sp => new AppStateService(new JsonFileStore<AppState>(
            paths.StateFile, AppStateJsonContext.Default.AppState, AppState.CurrentSchemaVersion, sp.GetRequiredService<TimeProvider>()), sp.GetRequiredService<TimeProvider>()));
    }

    private static void AddContent(IServiceCollection services, AppPaths paths)
    {
        // OWNER-DECISION (SPEC §13): the User-Agent carries only the app version, never an identifier (SPEC §10).
        services.AddSingleton(_ =>
        {
            var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PrettyDesk", AppConstants.DisplayVersion));
            return client;
        });
        services.AddSingleton(_ => TrustedKeys.CreateVerifier());
        services.AddSingleton(sp => new CatalogService(
            new CatalogServiceOptions(paths.BundledCatalog, paths.CatalogDirectory, AppConstants.ContentBaseUrl, AppConstants.Version),
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<CatalogSignatureVerifier>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<CatalogService>>()));
        services.AddSingleton<ICatalogProvider>(sp => sp.GetRequiredService<CatalogService>());

        services.AddSingleton(_ => new PackStore(paths.Packs));
        services.AddSingleton(_ => new UserImageStore(paths.UserImages));
        services.AddSingleton(sp => new FileDownloader(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<FileDownloader>>()));
        services.AddSingleton(sp => new ContentLibrary(
            sp.GetRequiredService<ICatalogProvider>(),
            sp.GetRequiredService<PackStore>(),
            sp.GetRequiredService<UserImageStore>(),
            sp.GetRequiredService<FileDownloader>(),
            new ContentLibraryOptions(Directory.Exists(paths.BundledStarter) ? paths.BundledStarter : null),
            () => sp.GetRequiredService<PrefetchCoordinator>().WantedPacks(),
            () => (long)(sp.GetRequiredService<ISettingsProvider>().Current.Content.MaxCacheGB * 1024 * 1024 * 1024),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ContentLibrary>>()));
        services.AddSingleton<IContentLibrary>(sp => sp.GetRequiredService<ContentLibrary>());
        services.AddSingleton<IContentBrowser>(sp => sp.GetRequiredService<ContentLibrary>());

        services.AddSingleton(sp => new RenderCache(
            paths.RenderCache,
            () => sp.GetRequiredService<AppStateService>().Current.LastApplied.Values.ToHashSet(StringComparer.OrdinalIgnoreCase),
            RenderCache.DefaultMaxBytes,
            sp.GetRequiredService<ILogger<RenderCache>>()));
        services.AddSingleton<WallpaperRenderer>();
        services.AddSingleton<IWallpaperRenderer>(sp => sp.GetRequiredService<WallpaperRenderer>());
    }

    private static void AddWindows(IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton<ProcessSource>();
        services.AddSingleton<IProcessSource>(sp => sp.GetRequiredService<ProcessSource>());
        services.AddSingleton<ProcessDetailsSource>();
        services.AddSingleton<IProcessDetailsSource>(sp => sp.GetRequiredService<ProcessDetailsSource>());
        services.AddSingleton<ForegroundWatcher>();
        services.AddSingleton<IForegroundSource>(sp => sp.GetRequiredService<ForegroundWatcher>());
        services.AddSingleton<SteamRunningAppWatcher>();
        services.AddSingleton<ISteamRunningAppSource>(sp => sp.GetRequiredService<SteamRunningAppWatcher>());
        services.AddSingleton<DesktopWallpaperService>();
        services.AddSingleton<IWallpaperSetter>(sp => sp.GetRequiredService<DesktopWallpaperService>());
        services.AddSingleton<IMonitorProvider>(sp => sp.GetRequiredService<DesktopWallpaperService>());
        services.AddSingleton<WindowsSystemState>();
        services.AddSingleton<ISystemState>(sp => sp.GetRequiredService<WindowsSystemState>());
        services.AddSingleton<EnvironmentConflictDetector>();
        services.AddSingleton<IEnvironmentConflictSource>(sp => sp.GetRequiredService<EnvironmentConflictDetector>());
        services.AddSingleton(sp => new WallpaperBackupService(
            sp.GetRequiredService<DesktopWallpaperService>(), sp.GetRequiredService<AppStateService>(), paths.Backup, paths.RenderCache,
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<WallpaperBackupService>>()));
        services.AddSingleton<IWallpaperBackup>(sp => sp.GetRequiredService<WallpaperBackupService>());
        services.AddSingleton<ShellMessageWindow>();
        services.AddSingleton<SessionEvents>();
        services.AddSingleton<UserNotificationStateSource>();
        services.AddSingleton<IInstalledGameScanner>(_ =>
        {
            using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = steamKey?.GetValue("SteamPath") as string;
            var epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            return new InstalledGameScanner(steamPath, epic);
        });
    }

    private static void AddRuntime(IServiceCollection services)
    {
        services.AddSingleton(sp => new RotationScheduler(sp.GetRequiredService<AppStateService>().Current.Rotation, sp.GetRequiredService<TimeProvider>(), Random.Shared));
        services.AddSingleton(sp => new WallpaperOrchestrator(
            sp.GetRequiredService<IMonitorProvider>(),
            sp.GetRequiredService<IWallpaperSetter>(),
            sp.GetRequiredService<IWallpaperRenderer>(),
            sp.GetRequiredService<IContentLibrary>(),
            sp.GetRequiredService<ISystemState>(),
            sp.GetRequiredService<ICatalogProvider>(),
            sp.GetRequiredService<ISettingsProvider>(),
            sp.GetRequiredService<RotationScheduler>(),
            sp.GetRequiredService<AppStateService>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<WallpaperOrchestrator>>(),
            sp.GetRequiredService<IWallpaperBackup>()));
        services.AddSingleton<IWallpaperController>(sp => sp.GetRequiredService<WallpaperOrchestrator>());

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsProvider>();
            var catalog = sp.GetRequiredService<ICatalogProvider>();
            return new DetectionService(
                sp.GetRequiredService<IProcessSource>(),
                sp.GetRequiredService<IForegroundSource>(),
                sp.GetRequiredService<ISteamRunningAppSource>(),
                DetectionConfigurator.Build(settings.Current, catalog.Current, sp.GetRequiredService<IProcessDetailsSource>()),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<DetectionService>>());
        });
        services.AddSingleton<IDetectionFeed>(sp => sp.GetRequiredService<DetectionService>());
        services.AddSingleton(sp => new DetectionConfigurator(
            sp.GetRequiredService<DetectionService>(), sp.GetRequiredService<ISettingsProvider>(), sp.GetRequiredService<ICatalogProvider>(), sp.GetRequiredService<IProcessDetailsSource>()));
        services.AddSingleton(sp => new PrefetchCoordinator(
            sp.GetRequiredService<ISettingsProvider>(), sp.GetRequiredService<ICatalogProvider>(), sp.GetRequiredService<IMonitorProvider>(),
            sp.GetRequiredService<IContentBrowser>(), sp.GetRequiredService<IInstalledGameScanner>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new UnknownGameMonitor(
            sp.GetRequiredService<IForegroundSource>(),
            UserNotificationStateSource.Query,
            exe => KnownExecutables.IsKnown(sp.GetRequiredService<ICatalogProvider>().Current, sp.GetRequiredService<ISettingsProvider>().Current, exe),
            new UnknownGameHinter(),
            sp.GetRequiredService<ISettingsProvider>(),
            sp.GetRequiredService<TimeProvider>()));
    }

    private static void AddUi(IServiceCollection services)
    {
        services.AddSingleton<IUiDispatcher, WpfDispatcher>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePicker, FilePickerService>();
        services.AddSingleton<IExternalLauncher, ExternalLauncher>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IRunningAppsProvider, RunningAppsAdapter>();
        services.AddSingleton<IUpdateService, NotSupportedUpdateService>();
        services.AddSingleton<IAppInfo, AppInfoService>();
        services.AddSingleton<IDiagnosticsExporter, DiagnosticsExporter>();
        services.AddSingleton<IRestoreService, RestoreService>();
        services.AddSingleton<IAppMaintenance, AppMaintenance>();
        services.AddSingleton<IInstalledGamesProvider, InstalledGamesProvider>();
        services.AddSingleton<IDetectionProtectedPacks>(sp => new ProtectedPacks(() => sp.GetRequiredService<PrefetchCoordinator>().WantedPacks()));

        services.AddSingleton<TrayViewModel>();
        services.AddSingleton<TrayService>();
        services.AddSingleton<INotifier>(sp => sp.GetRequiredService<TrayService>());
        services.AddSingleton(sp => new WindowManager(
            () => sp.GetRequiredService<ShellViewModel>(),
            () => sp.GetRequiredService<OnboardingViewModel>(),
            sp.GetRequiredService<ISettingsProvider>(),
            () => sp.GetRequiredService<INotifier>()));
        services.AddSingleton<IAppController>(sp => sp.GetRequiredService<WindowManager>());
        services.AddSingleton(sp => new NotificationCoordinator(
            sp.GetRequiredService<ISettingsProvider>(), sp.GetRequiredService<ICatalogProvider>(), sp.GetRequiredService<IContentBrowser>(),
            sp.GetRequiredService<IUpdateService>(), sp.GetRequiredService<INotifier>(), sp.GetRequiredService<IUiDispatcher>(),
            exe => sp.GetRequiredService<WindowManager>().ShowMainWindow()));

        // Pages are transient: each main-window session gets fresh view models that are disposed when the window closes (FR-APP-4).
        services.AddTransient<HomeViewModel>();
        services.AddTransient<DefaultsViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<GeneralSettingsViewModel>();
        services.AddTransient<DetectionSettingsViewModel>();
        services.AddTransient<MonitorSettingsViewModel>();
        services.AddTransient<StorageSettingsViewModel>();
        services.AddTransient<NotificationSettingsViewModel>();
        services.AddTransient<AdvancedSettingsViewModel>();
        services.AddTransient<RestoreViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<AddGameViewModel>(sp => new AddGameViewModel(
            sp.GetRequiredService<ISettingsProvider>(), sp.GetRequiredService<ICatalogProvider>(), sp.GetRequiredService<IRunningAppsProvider>(),
            sp.GetRequiredService<IFilePicker>(), sp.GetRequiredService<IUiDispatcher>(), existing => NewPicker(sp, existing)));
        services.AddTransient<LibraryViewModel>(sp => new LibraryViewModel(
            sp.GetRequiredService<ICatalogProvider>(), sp.GetRequiredService<ISettingsProvider>(), sp.GetRequiredService<IContentBrowser>(),
            sp.GetRequiredService<IContentLibrary>(), sp.GetRequiredService<IInstalledGamesProvider>(), sp.GetRequiredService<IUiDispatcher>(),
            (listing, close) => new GameDetailViewModel(
                listing, sp.GetRequiredService<ISettingsProvider>(), sp.GetRequiredService<ICatalogProvider>(), sp.GetRequiredService<IContentBrowser>(),
                sp.GetRequiredService<IContentLibrary>(), sp.GetRequiredService<IWallpaperController>(), sp.GetRequiredService<IMonitorProvider>(),
                sp.GetRequiredService<IDialogService>(), sp.GetRequiredService<IFilePicker>(), sp.GetRequiredService<IExternalLauncher>(),
                sp.GetRequiredService<IUiDispatcher>(), existing => NewPicker(sp, existing), close),
            () => sp.GetRequiredService<AddGameViewModel>()));
        services.AddTransient<ShellViewModel>(sp => new ShellViewModel(
            () => sp.GetRequiredService<HomeViewModel>(),
            () => sp.GetRequiredService<LibraryViewModel>(),
            () => sp.GetRequiredService<DefaultsViewModel>(),
            () => sp.GetRequiredService<SettingsViewModel>(),
            () => sp.GetRequiredService<AboutViewModel>()));
    }

    private static WallpaperPickerViewModel NewPicker(IServiceProvider sp, IEnumerable<string> existing) => new(
        sp.GetRequiredService<ICatalogProvider>().Current,
        sp.GetRequiredService<IContentLibrary>(),
        sp.GetRequiredService<IContentBrowser>(),
        sp.GetRequiredService<IFilePicker>(),
        existing);

    private sealed class ProtectedPacks(Func<IReadOnlySet<string>> source) : IDetectionProtectedPacks
    {
        public IReadOnlySet<string> PacksInUse() => source();
    }
}
