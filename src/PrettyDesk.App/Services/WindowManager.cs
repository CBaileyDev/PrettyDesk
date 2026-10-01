using System.Windows;
using PrettyDesk.App.Views;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Presentation.ViewModels;
using Wpf.Ui.Appearance;

namespace PrettyDesk.App.Services;

/// <summary>
/// Creates and releases the app's windows (FR-APP-4): closing the main window really closes it and disposes its view models,
/// which keeps idle memory low while PrettyDesk keeps running in the tray.
/// </summary>
public sealed class WindowManager : IAppController
{
    private readonly Func<ShellViewModel> _shell;
    private readonly Func<OnboardingViewModel> _onboarding;
    private readonly ISettingsProvider _settings;
    private readonly Func<INotifier> _notifier;
    private MainWindow? _main;
    private OnboardingWindow? _onboardingWindow;

    public WindowManager(Func<ShellViewModel> shell, Func<OnboardingViewModel> onboarding, ISettingsProvider settings, Func<INotifier> notifier)
    {
        _shell = shell;
        _onboarding = onboarding;
        _settings = settings;
        _notifier = notifier;
    }

    /// <summary>Raised when the user chooses Quit from the tray. The app shuts down in <see cref="App"/>.</summary>
    public event Action? QuitRequested;

    public void ShowMainWindow() => Run(() =>
    {
        if (_main is null)
        {
            var viewModel = _shell();
            _main = new MainWindow(viewModel);
            SystemThemeWatcher.Watch(_main);
            _main.Closed += (_, _) =>
            {
                viewModel.Dispose();
                _main = null;
                ShowTrayHintOnce();
            };
        }

        Reveal(_main);
    });

    public void ShowOnboarding() => Run(() =>
    {
        if (_onboardingWindow is null)
        {
            var viewModel = _onboarding();
            _onboardingWindow = new OnboardingWindow(viewModel);
            SystemThemeWatcher.Watch(_onboardingWindow);
            _onboardingWindow.Closed += (_, _) =>
            {
                _onboardingWindow = null;
                if (_settings.Current.General.OnboardingCompleted)
                {
                    ShowTrayHintOnce();
                }
            };
        }

        Reveal(_onboardingWindow);
    });

    public void Quit() => QuitRequested?.Invoke();

    /// <summary>Closes every window (used during shutdown).</summary>
    public void CloseAll() => Run(() =>
    {
        _main?.Close();
        _onboardingWindow?.Close();
    });

    private void ShowTrayHintOnce()
    {
        if (_settings.Current.General.TrayHintShown)
        {
            return;
        }

        _settings.Update(s => s.General.TrayHintShown = true);
        _notifier().Show(Strings.Tray_FirstHideTitle, Strings.Tray_FirstHideBody);
    }

    private static void Reveal(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private static void Run(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
