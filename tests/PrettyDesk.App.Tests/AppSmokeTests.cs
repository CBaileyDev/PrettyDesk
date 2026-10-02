using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PrettyDesk.App;
using PrettyDesk.App.Composition;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Tests.Support;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Presentation.ViewModels;
using Serilog;
using Shouldly;
using Xunit;

namespace PrettyDesk.App.Tests;

/// <summary>
/// Windows-only smoke tests that run the real WPF stack: they build the dependency graph with validation and instantiate every
/// page against its view model, failing on any WPF data-binding error (SPEC §7: "zero binding errors in debug output").
/// </summary>
public sealed class AppSmokeTests : IDisposable
{
    private readonly TempDir _dir = new();

    public AppSmokeTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows-only UI test");

    public void Dispose() => _dir.Dispose();

    private ServiceProvider BuildProvider()
    {
        Log.Logger = new LoggerConfiguration().CreateLogger();
        var paths = new AppPaths(_dir.Path);
        paths.EnsureCreated();
        return new ServiceCollection().AddPrettyDesk(paths).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public async Task Every_default_collection_has_a_hash_verified_wallpaper_and_thumbnail_without_network()
    {
        await using var provider = BuildProvider();
        var catalog = provider.GetRequiredService<PrettyDesk.Core.Catalog.CatalogService>();
        catalog.LoadInitial();
        catalog.Current.Collections.Count.ShouldBeGreaterThanOrEqualTo(14);
        var content = provider.GetRequiredService<PrettyDesk.Core.Content.ContentLibrary>();
        foreach (var collection in catalog.Current.Collections)
        {
            var pack = catalog.Current.FindPack(collection.PackId).ShouldNotBeNull();
            var starter = pack.Wallpapers.Single(w => w.Starter);
            var asset = content.TryGetAsset(starter.Id).ShouldNotBeNull();
            asset.Variants.ShouldContainKey("16x9");
            var variant = asset.Variants["16x9"];
            variant.Width.ShouldBe(3840);
            variant.Height.ShouldBe(2160);
            File.Exists(variant.Path).ShouldBeTrue();
            File.Exists(content.GetThumbnailPath(starter.Id)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task The_dependency_graph_builds_and_every_root_service_resolves()
    {
        await using var provider = BuildProvider();

        foreach (var type in new[]
        {
            typeof(IWallpaperController), typeof(IContentBrowser), typeof(IDetectionFeed), typeof(IAppController), typeof(INotifier),
            typeof(IRestoreService), typeof(IAppMaintenance), typeof(ShellViewModel), typeof(OnboardingViewModel), typeof(TrayViewModel),
        })
        {
            Should.NotThrow(() => provider.GetRequiredService(type), type.Name);
        }
    }

    [Fact]
    public async Task Every_Liquid_Glass_colorway_and_screen_ratio_is_available_offline()
    {
        await using var provider = BuildProvider();
        var catalog = provider.GetRequiredService<PrettyDesk.Core.Catalog.CatalogService>();
        catalog.LoadInitial();
        var pack = catalog.Current.FindPack("default.ios-glass").ShouldNotBeNull();
        pack.Wallpapers.Count.ShouldBe(3);
        var content = provider.GetRequiredService<PrettyDesk.Core.Content.ContentLibrary>();
        foreach (var wallpaper in pack.Wallpapers)
        {
            var asset = content.TryGetAsset(wallpaper.Id).ShouldNotBeNull();
            asset.Variants.Keys.Order().ShouldBe(new[] { "16x10", "16x9", "21x9", "32x9", "3x2", "9x16" }.Order());
            foreach (var variant in asset.Variants.Values)
            {
                File.Exists(variant.Path).ShouldBeTrue();
            }
        }
    }

    [Fact]
    public async Task Rocket_League_car_wallpapers_are_available_offline_in_the_runtime_catalog()
    {
        await using var provider = BuildProvider();
        var catalog = provider.GetRequiredService<PrettyDesk.Core.Catalog.CatalogService>();
        catalog.LoadInitial();
        var pack = catalog.Current.FindPack("game.rocket-league").ShouldNotBeNull();
        var content = provider.GetRequiredService<PrettyDesk.Core.Content.ContentLibrary>();
        foreach (var id in new[] { "rocket-league.octane-aerial", "rocket-league.fennec-freeplay", "rocket-league.batmobile-rain" })
        {
            pack.Wallpapers.ShouldContain(w => w.Id == id);
            var asset = content.TryGetAsset(id).ShouldNotBeNull();
            asset.Variants.ShouldContainKey("16x9");
            File.Exists(asset.Variants["16x9"].Path).ShouldBeTrue();
            File.Exists(content.GetThumbnailPath(id)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Orchestrator_and_content_library_are_shared_singletons()
    {
        await using var provider = BuildProvider();

        provider.GetRequiredService<IWallpaperController>().ShouldBeSameAs(provider.GetRequiredService<IWallpaperController>());
        provider.GetRequiredService<IContentBrowser>().ShouldBeSameAs(provider.GetRequiredService<IContentLibrary>());
    }

    [Fact]
    public void Every_page_and_the_onboarding_view_instantiate_without_binding_errors()
    {
        var errors = new List<string>();
        var listener = new BindingErrorListener(errors);
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunOnSta(errors);
            }
#pragma warning disable CA1031 // Reported to the asserting thread below.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60)).ShouldBeTrue("the UI smoke test timed out");

        failure.ShouldBeNull(failure?.ToString());
        errors.ShouldBeEmpty(string.Join("\n", errors));
    }

    private void RunOnSta(List<string> errors)
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Light });
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PrettyDesk;component/Resources/Styles.xaml") });
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(System.Windows.Media.Color.FromRgb(0, 122, 255), Wpf.Ui.Appearance.ApplicationTheme.Light);
        PrettyDesk.App.Services.AppAppearance.ApplyPalette(Wpf.Ui.Appearance.ApplicationTheme.Light);
        app.Resources.Add(new DataTemplateKey(typeof(HomeViewModel)), new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(PrettyDesk.App.Views.HomePage)) });
        app.Resources.Add(new DataTemplateKey(typeof(DefaultsViewModel)), new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(PrettyDesk.App.Views.DefaultsPage)) });
        app.Resources.Add(new DataTemplateKey(typeof(LibraryViewModel)), new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(PrettyDesk.App.Views.LibraryPage)) });
        app.Resources.Add(new DataTemplateKey(typeof(SettingsViewModel)), new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(PrettyDesk.App.Views.SettingsPage)) });
        app.Resources.Add(new DataTemplateKey(typeof(AboutViewModel)), new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(PrettyDesk.App.Views.AboutPage)) });

        var provider = BuildProvider();
        try
        {
            provider.GetRequiredService<PrettyDesk.Core.Catalog.CatalogService>().LoadInitial();
            ShowEveryPage(provider);
        }
        finally
        {
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _ = errors;
    }

    private static void VerifyAcrylicMaterialAndFocus(PrettyDesk.App.Views.MainWindow main)
    {
        using var effects = new PrettyDesk.Windows.DesktopEffectsPreferences();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) && effects.TransparencyEnabled && !SystemParameters.HighContrast)
        {
            main.NativeAcrylicEnabled.ShouldBeTrue("native acrylic must enable on a supported desktop");
        }
        if (!main.NativeAcrylicEnabled) { return; }
        main.WindowBackdropType.ShouldBe(Wpf.Ui.Controls.WindowBackdropType.Acrylic);
        // Windows may deny foreground activation while the user is interacting
        // with another app. Record that native-test limit rather than fake focus.
        if (!main.Activate())
        {
            WriteFocusEvidence(false, false, false);
            return;
        }
        Pump();
        var panel = Application.Current.Resources["DeskPanelBrush"];
        var foreground = Application.Current.Resources["DeskInkBrush"];
        var focusTarget = new Window { Title = "PrettyDesk focus verification", Width = 240, Height = 120, ShowInTaskbar = false };
        try
        {
            focusTarget.Show();
            if (!focusTarget.Activate())
            {
                WriteFocusEvidence(true, false, false);
                return;
            }
            Pump();
            main.IsActive.ShouldBeFalse("the other window must receive real input focus");
            main.NativeAcrylicEnabled.ShouldBeTrue();
            main.WindowBackdropType.ShouldBe(Wpf.Ui.Controls.WindowBackdropType.Acrylic);
            Application.Current.Resources["DeskPanelBrush"].ShouldBeSameAs(panel);
            Application.Current.Resources["DeskInkBrush"].ShouldBeSameAs(foreground);
            CaptureIfRequested(main, "Main-Inactive-Acrylic");
        }
        finally { focusTarget.Close(); }
        var reactivated = main.Activate();
        Pump();
        main.WindowBackdropType.ShouldBe(Wpf.Ui.Controls.WindowBackdropType.Acrylic);
        WriteFocusEvidence(true, true, reactivated);
    }

    private static void WriteFocusEvidence(bool activated, bool deactivated, bool reactivated)
    {
        var directory = Environment.GetEnvironmentVariable("PRETTYDESK_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) { return; }
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "focus-transition.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            NativeAcrylicEnabled = true,
            Activated = activated,
            Deactivated = deactivated,
            Reactivated = reactivated,
            VisualPixelsVerified = false,
        }));
    }

    private static void ShowEveryPage(ServiceProvider provider)
    {
        var shell = provider.GetRequiredService<ShellViewModel>();
        var main = new PrettyDesk.App.Views.MainWindow(shell);
        main.Show();
        Pump();
        VerifyAcrylicMaterialAndFocus(main);
        MeasureNavigationIfRequested(shell, main);
        VerifyLibraryNavigation(shell, main);
        CaptureIfRequested(main, "Main-Home");
        CaptureLogoIfRequested((FrameworkElement)main.FindName("BrandLogo"), "Brand-Logo-Light");
        var clock = provider.GetRequiredService<PrettyDesk.App.Services.DesktopClockService>();
        clock.Start();
        var settings = provider.GetRequiredService<ISettingsProvider>();
        settings.Update(s => s.General.DesktopClock = true);
        Pump();
        var surface = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "PrettyDesk desktop clock");
        surface.IsVisible.ShouldBeTrue();
        surface.IsActive.ShouldBeFalse();
        surface.ShowInTaskbar.ShouldBeFalse();
        surface.IsHitTestVisible.ShouldBeFalse();
        CaptureIfRequested(surface, "Desktop-Clock");
        settings.Update(s => s.General.DesktopClock = false);
        Pump();
        surface.IsVisible.ShouldBeFalse();
        clock.Dispose();
        shell.NavigateToCommand.Execute(PageKind.Defaults);
        Pump();
        CaptureIfRequested(main, "Main-Wallpapers");
        main.Width = main.MinWidth;
        main.Height = main.MinHeight;
        Pump();
        CaptureIfRequested(main, "Main-Minimum");
        shell.NavigateToCommand.Execute(PageKind.Library);
        Pump();
        CaptureIfRequested(main, "Main-Minimum-Library");
        shell.NavigateToCommand.Execute(PageKind.Home);
        Pump();
        CaptureIfRequested(main, "Main-Minimum-Home");
        shell.NavigateToCommand.Execute(PageKind.Defaults);
        Pump();
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
        PrettyDesk.App.Services.AppAppearance.ApplyPalette(Wpf.Ui.Appearance.ApplicationTheme.Dark);
        main.Width = 1160;
        main.Height = 780;
        Pump();
        CaptureIfRequested(main, "Main-Dark");
        CaptureLogoIfRequested((FrameworkElement)main.FindName("BrandLogo"), "Brand-Logo-Dark");
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
        PrettyDesk.App.Services.AppAppearance.ApplyPalette(Wpf.Ui.Appearance.ApplicationTheme.Light);
        main.Close();
        var pages = new (string Name, Func<UserControl> Create)[]
        {
            ("Home", () => new PrettyDesk.App.Views.HomePage { DataContext = provider.GetRequiredService<HomeViewModel>() }),
            ("Library", () => new PrettyDesk.App.Views.LibraryPage { DataContext = provider.GetRequiredService<LibraryViewModel>() }),
            ("Defaults", () => new PrettyDesk.App.Views.DefaultsPage { DataContext = provider.GetRequiredService<DefaultsViewModel>() }),
            ("Settings", () => new PrettyDesk.App.Views.SettingsPage { DataContext = provider.GetRequiredService<SettingsViewModel>() }),
            ("About", () => new PrettyDesk.App.Views.AboutPage { DataContext = provider.GetRequiredService<AboutViewModel>() }),
        };

        foreach (var (name, create) in pages)
        {
            var view = create();
            var window = new Window { Content = view, Width = 1000, Height = 700, ShowInTaskbar = false };
            window.Show();
            Pump();
            CaptureIfRequested(window, name);
            if (view is PrettyDesk.App.Views.HomePage home)
            {
                ((ScrollViewer)home.Content).ScrollToEnd();
                Pump();
                CaptureIfRequested(window, "Home-Displays");
            }
            window.Close();
            (view.DataContext as IDisposable)?.Dispose();
            _ = name;
        }

        var onboarding = new PrettyDesk.App.Views.OnboardingWindow(provider.GetRequiredService<OnboardingViewModel>());
        onboarding.Show();
        Pump();
        CaptureIfRequested(onboarding, "Onboarding");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRETTYDESK_UI_CAPTURE_DIR")))
        {
            ((OnboardingViewModel)onboarding.DataContext).Step = OnboardingStep.Startup;
            Pump();
            CaptureIfRequested(onboarding, "Onboarding-Startup");
        }
        onboarding.Close();

        // Isolated download-step fixture: no real installation scan or network request.
        var displays = Substitute.For<IMonitorProvider>();
        displays.GetMonitors().Returns(new List<MonitorInfo> { new("one", 0, 0, 2560, 1440, true), new("two", 2560, 0, 2560, 1440, false) });
        var content = Substitute.For<IContentBrowser>();
        content.GetDownloadPlan(Arg.Any<string>(), Arg.Any<IReadOnlyList<MonitorInfo>>()).Returns(call =>
            new PrettyDesk.Core.Content.PackDownloadPlan(true, true, call.ArgAt<string>(0) == "game.rocket-league" ? 0 : 24500000, ["16x9"], false));
        content.GetPackState(Arg.Any<string>()).Returns(call => new PrettyDesk.Core.Content.PackProgress(call.ArgAt<string>(0), PrettyDesk.Core.Content.PackStateKind.NotDownloaded, 0));
        var downloads = new OnboardingViewModel(provider.GetRequiredService<ISettingsProvider>(), provider.GetRequiredService<ICatalogProvider>(),
            Substitute.For<IInstalledGamesProvider>(), Substitute.For<IStartupService>(), Substitute.For<IEnvironmentConflictSource>(), content, displays, provider.GetRequiredService<IUiDispatcher>());
        downloads.Games.Add(new("rocket-league", "Rocket League"));
        downloads.Games.Add(new("fortnite", "Fortnite"));
        downloads.Step = OnboardingStep.Games;
        downloads.NextCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        var downloadWindow = new PrettyDesk.App.Views.OnboardingWindow(downloads);
        downloadWindow.Show();
        Pump();
        CaptureIfRequested(downloadWindow, "Onboarding-Wallpapers");
        downloadWindow.Width = 640;
        downloadWindow.Height = 520;
        Pump();
        CaptureIfRequested(downloadWindow, "Onboarding-Wallpapers-Minimum");
        downloadWindow.Width = 760;
        downloadWindow.Height = 600;
        content.GetPackState("game.fortnite").Returns(new PrettyDesk.Core.Content.PackProgress("game.fortnite", PrettyDesk.Core.Content.PackStateKind.Downloading, 0.42));
        content.ProgressChanged += Raise.Event<Action<PrettyDesk.Core.Content.PackProgress>>(new PrettyDesk.Core.Content.PackProgress("game.fortnite", PrettyDesk.Core.Content.PackStateKind.Downloading, 0.42));
        Pump();
        ((ScrollViewer)downloadWindow.FindName("WallpaperList")).ScrollToBottom();
        Pump();
        CaptureIfRequested(downloadWindow, "Onboarding-Wallpapers-Downloading");
        content.GetPackState("game.fortnite").Returns(new PrettyDesk.Core.Content.PackProgress("game.fortnite", PrettyDesk.Core.Content.PackStateKind.Failed, 0));
        content.ProgressChanged += Raise.Event<Action<PrettyDesk.Core.Content.PackProgress>>(new PrettyDesk.Core.Content.PackProgress("game.fortnite", PrettyDesk.Core.Content.PackStateKind.Failed, 0));
        Pump();
        CaptureIfRequested(downloadWindow, "Onboarding-Wallpapers-Failed");
        content.GetDownloadPlan("game.fortnite", Arg.Any<IReadOnlyList<MonitorInfo>>()).Returns(new PrettyDesk.Core.Content.PackDownloadPlan(true, false, 24500000, ["16x9"], false));
        content.PackChanged += Raise.Event<Action<string>>("game.fortnite");
        Pump();
        CaptureIfRequested(downloadWindow, "Onboarding-Wallpapers-Offline");
        downloads.Step = OnboardingStep.Games;
        downloads.Games.Clear();
        downloads.NextCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Pump();
        CaptureIfRequested(downloadWindow, "Onboarding-Wallpapers-Empty");
        downloadWindow.Close();
    }

    private static void CaptureLogoIfRequested(FrameworkElement logo, string name)
    {
        var directory = Environment.GetEnvironmentVariable("PRETTYDESK_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) { return; }
        Directory.CreateDirectory(directory);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(logo.ActualWidth * 6),
            (int)Math.Ceiling(logo.ActualHeight * 6), 576, 576, System.Windows.Media.PixelFormats.Pbgra32);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new System.Windows.Media.VisualBrush(logo), null, new Rect(0, 0, logo.ActualWidth, logo.ActualHeight));
        }
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }

    private static void MeasureNavigationIfRequested(ShellViewModel shell, Window window)
    {
        var directory = Environment.GetEnvironmentVariable("PRETTYDESK_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) { return; }
        Directory.CreateDirectory(directory);
        var samples = new List<object>();
        for (var pass = 0; pass < 4; pass++)
        {
            foreach (var page in new[] { PageKind.Library, PageKind.Defaults, PageKind.Settings, PageKind.About, PageKind.Home })
            {
                var clock = Stopwatch.StartNew();
                shell.NavigateToCommand.Execute(page);
                window.UpdateLayout();
                Pump();
                samples.Add(new { Pass = pass, Page = page.ToString(), LayoutMilliseconds = clock.Elapsed.TotalMilliseconds });
            }
        }
        File.WriteAllText(Path.Combine(directory, "navigation-timing.json"), System.Text.Json.JsonSerializer.Serialize(samples));
    }

    private static void VerifyLibraryNavigation(ShellViewModel shell, PrettyDesk.App.Views.MainWindow window)
    {
        shell.NavigateToCommand.Execute(PageKind.Library);
        window.UpdateLayout();
        Pump();
        var host = (PrettyDesk.App.Views.MotionContentControl)window.FindName("PageHost");
        var library = ((Grid)host.Content).Children.OfType<PrettyDesk.App.Views.LibraryPage>().Single();
        var tiles = (ListView)library.FindName("GameTiles");
        tiles.Items.Count.ShouldBeGreaterThan(20);
        var realized = Enumerable.Range(0, tiles.Items.Count).Count(i => tiles.ItemContainerGenerator.ContainerFromIndex(i) is not null);
        realized.ShouldBeLessThan(tiles.Items.Count, "off-screen tiles should not all be built");
        tiles.ScrollIntoView(tiles.Items[^1]);
        window.UpdateLayout();
        Pump();
        tiles.ItemContainerGenerator.ContainerFromIndex(tiles.Items.Count - 1).ShouldNotBeNull();
        shell.NavigateToCommand.Execute(PageKind.Home);
        shell.NavigateToCommand.Execute(PageKind.Library);
        window.UpdateLayout();
        Pump();
        ((Grid)host.Content).Children.OfType<PrettyDesk.App.Views.LibraryPage>().Single().ShouldBeSameAs(library);
        tiles.ItemContainerGenerator.ContainerFromIndex(tiles.Items.Count - 1).ShouldNotBeNull("returning should preserve the scroll position");
        tiles.ScrollIntoView(tiles.Items[0]);
        shell.NavigateToCommand.Execute(PageKind.Home);
        window.UpdateLayout();
        Pump();
    }

    private static void CaptureIfRequested(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("PRETTYDESK_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        // Native Mica and transparent card brushes are composited by DWM, outside RenderTargetBitmap.
        // Supply the dark theme's opaque base before capturing the real content visual.
        var surface = new System.Windows.Media.DrawingVisual();
        using (var drawing = surface.RenderOpen())
        {
            var bounds = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
            drawing.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 246, 250)), null, bounds);
        }
        bitmap.Render(surface);
        bitmap.Render((System.Windows.Media.Visual)window.Content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }

    private static void Pump() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

    private sealed class BindingErrorListener(List<string> errors) : TraceListener
    {
        private readonly System.Text.StringBuilder _line = new();

        public override void Write(string? message) => _line.Append(message);

        // The WPF-UI VirtualizingGridView realizes one ListViewItem before it is parented; its default style's
        // content-alignment FindAncestor bindings log once and re-resolve when the item is attached.
        private static bool IsTransientContainerAlignment(string text) =>
            text.Contains("AncestorType='System.Windows.Controls.ItemsControl'", StringComparison.Ordinal)
            && text.Contains("target element is 'ListViewItem'", StringComparison.Ordinal)
            && (text.Contains("Path=HorizontalContentAlignment", StringComparison.Ordinal)
                || text.Contains("Path=VerticalContentAlignment", StringComparison.Ordinal));

        public override void WriteLine(string? message)
        {
            _line.Append(message);
            var text = _line.ToString();
            _line.Clear();
            if (text.Contains("System.Windows.Data Error", StringComparison.Ordinal) && !IsTransientContainerAlignment(text))
            {
                lock (errors)
                {
                    errors.Add(text);
                }
            }
        }
    }
}
