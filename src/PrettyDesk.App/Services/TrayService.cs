using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Win32;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Presentation.ViewModels;

namespace PrettyDesk.App.Services;

/// <summary>
/// The tray icon and its menu (FR-APP-2): a status line, Next wallpaper, Pause ▸ (1 hour / until resumed) or Resume, Open
/// PrettyDesk, Quit. Double-click opens the window. The glyph adapts to a light or dark taskbar.
/// </summary>
public sealed class TrayService : INotifier, IDisposable
{
    private readonly TrayViewModel _viewModel;
    private TaskbarIcon? _icon;
    private MenuItem? _status;
    private MenuItem? _pause;
    private MenuItem? _resume;
    private MenuItem? _next;
    private Action? _pendingClick;

    public TrayService(TrayViewModel viewModel) => _viewModel = viewModel;

    public void Start()
    {
        _status = new MenuItem { IsEnabled = false, Header = _viewModel.StatusLine };
        _next = new MenuItem { Header = Strings.Tray_Next, Command = _viewModel.NextCommand };
        _pause = new MenuItem { Header = Strings.Tray_Pause };
        _pause.Items.Add(new MenuItem { Header = Strings.Tray_PauseOneHour, Command = _viewModel.PauseOneHourCommand });
        _pause.Items.Add(new MenuItem { Header = Strings.Tray_PauseUntilResumed, Command = _viewModel.PauseUntilResumedCommand });
        _resume = new MenuItem { Header = Strings.Tray_Resume, Command = _viewModel.ResumeCommand };
        var open = new MenuItem { Header = Strings.Tray_Open, Command = _viewModel.OpenCommand, FontWeight = FontWeights.SemiBold };
        var quit = new MenuItem { Header = Strings.Tray_Quit, Command = _viewModel.QuitCommand };

        var menu = new ContextMenu();
        menu.Items.Add(_status);
        menu.Items.Add(new Separator());
        menu.Items.Add(_next);
        menu.Items.Add(_pause);
        menu.Items.Add(_resume);
        menu.Items.Add(new Separator());
        menu.Items.Add(open);
        menu.Items.Add(quit);

        _icon = new TaskbarIcon
        {
            ToolTipText = Strings.Tray_Tooltip,
            ContextMenu = menu,
            NoLeftClickDelay = true,
            Icon = LoadIcon(),
        };
        _icon.TrayMouseDoubleClick += (_, _) => _viewModel.OpenCommand.Execute(null);
        _icon.TrayBalloonTipClicked += (_, _) =>
        {
            var click = _pendingClick;
            _pendingClick = null;
            click?.Invoke();
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);

        ApplyViewModel();
        _viewModel.PropertyChanged += (_, _) => ApplyViewModel();
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    /// <summary>Shows a tray notification. The click callback runs only if the user clicks that notification.</summary>
    public void Show(string title, string body, Action? onClick = null)
    {
        if (_icon is null)
        {
            return;
        }

        _pendingClick = onClick;
        _icon.ShowNotification(title, body, NotificationIcon.None);
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _icon?.Dispose();
        _icon = null;
    }

    private void ApplyViewModel()
    {
        if (_status is null || _pause is null || _resume is null || _next is null)
        {
            return;
        }

        _status.Header = _viewModel.StatusLine;
        _pause.Visibility = _viewModel.IsPaused ? Visibility.Collapsed : Visibility.Visible;
        _pause.IsEnabled = _viewModel.CanPause;
        _resume.Visibility = _viewModel.IsPaused ? Visibility.Visible : Visibility.Collapsed;
        _next.IsEnabled = _viewModel.CanSkip;
        if (_icon is not null)
        {
            _icon.ToolTipText = Strings.Tray_Tooltip + " — " + _viewModel.StatusLine;
        }
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color && _icon is not null)
        {
            Application.Current?.Dispatcher.BeginInvoke(() => _icon.Icon = LoadIcon());
        }
    }

    /// <summary>A dark glyph for light taskbars and a white one for dark taskbars (FR-APP-2).</summary>
    private static Icon LoadIcon()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var taskbarIsLight = key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        var name = taskbarIsLight ? "tray-for-light-taskbar.ico" : "tray-for-dark-taskbar.ico";
        var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"))?.Stream
            ?? throw new InvalidOperationException("The tray icon resource is missing.");
        using (stream)
        {
            // Ask for the size the shell actually draws (DPI-aware) instead of letting Windows scale a large frame.
            var size = PrettyDesk.Windows.ShellMetrics.SmallIconSizePixels();
            return new Icon(stream, size, size);
        }
    }
}
