using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrettyDesk.App.Composition;
using PrettyDesk.App.Services;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Presentation.Resources;
using Serilog;
using Wpf.Ui.Appearance;

namespace PrettyDesk.App;

public partial class App : Application, IAsyncDisposable
{
    private readonly SingleInstance _instance;
    private readonly string[] _args;
    private ServiceProvider? _provider;
    private AppRuntime? _runtime;
    private AppPaths? _paths;

    public App(SingleInstance instance, string[] args)
    {
        _instance = instance;
        _args = args;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _paths = new AppPaths();
        _paths.EnsureCreated();
        Log.Logger = Logging.Create(_paths);
        InstallGlobalHandlers();
        SessionEnding += OnSessionEnding;
        AppAppearance.Initialize();

        try
        {
            _provider = new ServiceCollection().AddPrettyDesk(_paths).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            AppAppearance.Bind(_provider.GetRequiredService<ISettingsProvider>());
            _runtime = new AppRuntime(_provider);
            _runtime.QuitRequested += OnQuitRequested;
            _runtime.StartUi();
#if PRETTYDESK_ACCEPTANCE
            AcceptanceHarness.RecordTrayReady(Program.StartupClock.Elapsed.TotalMilliseconds);
#endif
            _instance.Listen(() => Dispatcher.BeginInvoke(() => _provider.GetRequiredService<WindowManager>().ShowMainWindow()));

            var background = _args.Contains(AppConstants.BackgroundArgument, StringComparer.OrdinalIgnoreCase);
            var settings = _provider.GetRequiredService<ISettingsProvider>().Current;
            if (!settings.General.OnboardingCompleted)
            {
                _provider.GetRequiredService<WindowManager>().ShowOnboarding();
            }
            else if (!background)
            {
                _provider.GetRequiredService<WindowManager>().ShowMainWindow();
            }

            await _runtime.StartEngineAsync();
#if PRETTYDESK_ACCEPTANCE
            // A bounded acceptance run exits through the real Quit path, including wallpaper restoration.
            if (int.TryParse(Environment.GetEnvironmentVariable("PRETTYDESK_ACCEPTANCE_QUIT_AFTER_SECONDS"), out var seconds) && seconds > 0)
            {
                var exitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                exitTimer.Tick += (_, _) =>
                {
                    exitTimer.Stop();
                    OnQuitRequested();
                };
                exitTimer.Start();
            }
#endif
        }
#pragma warning disable CA1031 // NFR-13: a startup failure is shown in human words; the log has the details.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Error(ex, "Startup failed");
            MessageBox.Show(Strings.Error_StartupBody, Strings.Error_StartupTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_runtime is not null)
        {
            await _runtime.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppAppearance.Shutdown();
        base.OnExit(e);
    }

    private async void OnQuitRequested()
    {
        try
        {
            _provider?.GetRequiredService<WindowManager>().CloseAll();
            if (_runtime is not null)
            {
                await _runtime.DisposeAsync();
            }
        }
#pragma warning disable CA1031 // Exiting: nothing useful can be done with a cleanup failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Warning(ex, "Cleanup on quit failed");
        }
        finally
        {
            Log.CloseAndFlush();
            _instance.Dispose();
            Shutdown();
        }
    }

    /// <summary>
    /// Logoff or shutdown: we use <c>OnExplicitShutdown</c>, so without this the stop path (restore-on-exit, state flush) never
    /// runs. Bounded so a stuck COM call cannot make Windows wait on us.
    /// </summary>
    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        try
        {
            if (_runtime is null || _provider is null)
            {
                return;
            }

            var runtime = _runtime;
            var provider = _provider;
            Task.Run(async () =>
            {
                await runtime.StopAsync();
                provider.GetRequiredService<PrettyDesk.Core.Settings.AppStateService>().SaveNow();
            }).Wait(TimeSpan.FromSeconds(5));
        }
#pragma warning disable CA1031 // Session is ending: nothing useful can be done with a cleanup failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Warning(ex, "Cleanup on session end failed");
        }
    }

    /// <summary>NFR-13: no unhandled exception may crash the tray. Log it and keep running.</summary>
    private void InstallGlobalHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }
}
