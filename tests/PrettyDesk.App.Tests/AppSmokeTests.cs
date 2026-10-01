using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
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
    public void The_dependency_graph_builds_and_every_root_service_resolves()
    {
        using var provider = BuildProvider();

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
    public void Orchestrator_and_content_library_are_shared_singletons()
    {
        using var provider = BuildProvider();

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
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PrettyDesk;component/Resources/Styles.xaml") });

        using var provider = BuildProvider();
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
            window.Close();
            (view.DataContext as IDisposable)?.Dispose();
            _ = name;
        }

        var onboarding = new PrettyDesk.App.Views.OnboardingWindow(provider.GetRequiredService<OnboardingViewModel>());
        onboarding.Show();
        Pump();
        onboarding.Close();
        _ = errors;
    }

    private static void Pump() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

    private sealed class BindingErrorListener(List<string> errors) : TraceListener
    {
        private readonly System.Text.StringBuilder _line = new();

        public override void Write(string? message) => _line.Append(message);

        public override void WriteLine(string? message)
        {
            _line.Append(message);
            var text = _line.ToString();
            _line.Clear();
            if (text.Contains("System.Windows.Data Error", StringComparison.Ordinal))
            {
                lock (errors)
                {
                    errors.Add(text);
                }
            }
        }
    }
}
