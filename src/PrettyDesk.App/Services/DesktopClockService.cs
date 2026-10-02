using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Windows;

namespace PrettyDesk.App.Services;

/// <summary>Opt-in desktop clock, Windows media metadata and transient playback spectrum; never redraws wallpaper.</summary>
public sealed class DesktopClockService : IDisposable
{
    private readonly ISettingsProvider _settings;
    private readonly IMonitorProvider _monitors;
    private readonly ISystemState _system;
    private readonly DetectionService _detection;
    private readonly TimeProvider _time;
    private readonly PlaybackVisualizerSource _visualizer;
    private readonly NowPlayingSource _nowPlaying;
    private TextBlock? _media;
    private StackPanel? _spectrum;
    private readonly List<Rectangle> _bars = [];
    private DateTimeOffset _nextMediaRead;
    private DateTimeOffset _nextDeviceCheck;
    private bool _readingMedia;
    private bool _visualizing;
    private TimeSpan _interval;
    private Window? _window;
    private TextBlock? _label;
    private ITimer? _timer;
    private bool _locked;
    private bool _disposed;
    private bool _started;

    public DesktopClockService(ISettingsProvider settings, IMonitorProvider monitors, ISystemState system, DetectionService detection, TimeProvider time, NowPlayingSource nowPlaying)
    {
        _settings = settings;
        _monitors = monitors;
        _system = system;
        _detection = detection;
        _time = time;
        _nowPlaying = nowPlaying;
        _visualizer = new PlaybackVisualizerSource(time);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        _settings.Changed += Refresh;
        _monitors.Changed += Refresh;
        _system.Changed += Refresh;
        _detection.ActiveChanged += OnActiveChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemParameters.StaticPropertyChanged += OnAppearanceChanged;
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Changed -= Refresh;
        _monitors.Changed -= Refresh;
        _system.Changed -= Refresh;
        _detection.ActiveChanged -= OnActiveChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemParameters.StaticPropertyChanged -= OnAppearanceChanged;
        _timer?.Dispose();
        _visualizer.Dispose();
        _timer = null;
        Application.Current?.Dispatcher.Invoke(() => _window?.Close());
    }

    private void OnAppearanceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();

    private void OnActiveChanged(ActiveGameChange change) => Refresh();

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff)
        {
            _locked = true;
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon)
        {
            _locked = false;
        }
        Refresh();
    }

    private void Refresh() => Application.Current.Dispatcher.InvokeAsync(() =>
    {
        if (_disposed)
        {
            return;
        }

        var general = _settings.Current.General;
        if (!DesktopSurfacePolicy.ShouldShow(general.DesktopClock || general.DesktopNowPlaying || general.DesktopVisualizer, _detection.Active is not null, _locked, _system.IsBatterySaverOn))
        {
            _window?.Hide();
            _timer?.Dispose();
            _timer = null;
            _visualizer.Stop();
            _visualizing = false;
            return;
        }

        var monitors = _monitors.GetMonitors();
        var monitor = monitors.FirstOrDefault(m => m.Id == general.ClockMonitorId) ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? (monitors.Count > 0 ? monitors[0] : null);
        if (monitor is null)
        {
            _window?.Hide();
            _timer?.Dispose();
            _timer = null;
            _visualizer.Stop();
            return;
        }

        if (_window is null)
        {
            _label = new TextBlock { FontSize = 30, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, Margin = new Thickness(16), FontFamily = new FontFamily("Segoe UI Variable, Segoe UI") };
            _window = new Window
            {
                Width = 300,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                Title = "PrettyDesk desktop clock",
                Content = new Border { CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(220, 24, 24, 24)), Child = _label },
            };
            _window.SourceInitialized += (_, _) => DesktopSurfaceWindow.Configure(new WindowInteropHelper(_window).Handle);
            var stack = new StackPanel();
            stack.Children.Add(_label);
            _media = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxHeight = 64, Margin = new Thickness(12), Text = Strings.Widget_NothingPlaying };
            stack.Children.Add(_media);
            var spectrum = new StackPanel { Orientation = Orientation.Horizontal, Height = 44, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12) };
            _spectrum = spectrum;
            for (var i = 0; i < Core.Imaging.AudioSpectrum.BandCount; i++)
            {
                var bar = new Rectangle { Width = 14, Height = 2, Margin = new Thickness(3, 0, 3, 0), Fill = Brushes.LightSkyBlue, VerticalAlignment = VerticalAlignment.Bottom };
                _bars.Add(bar);
                spectrum.Children.Add(bar);
            }

            stack.Children.Add(spectrum);
            ((Border)_window.Content).Child = stack;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }
        DesktopSurfaceWindow.Place(new WindowInteropHelper(_window).Handle, monitor.Left + 32, monitor.Top + 32);
        UpdateContents();
        var animate = general.DesktopVisualizer && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        _spectrum!.Visibility = animate ? Visibility.Visible : Visibility.Collapsed;
        var interval = animate ? TimeSpan.FromMilliseconds(67) : general.DesktopNowPlaying ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(1);
        if (_timer is null || _visualizing != animate || interval != _interval)
        {
            _timer?.Dispose();
            _visualizing = animate;
            _interval = interval;
            _timer = _time.CreateTimer(_ => QueueFrame(), null, interval, interval);
        }
    });

    private int _framePending;

    private void QueueFrame()
    {
        if (Interlocked.Exchange(ref _framePending, 1) != 0)
        {
            return;
        }

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref _framePending, 0);
            if (!_disposed && _window?.IsVisible == true)
            {
                UpdateContents();
            }
        });
    }

    private void UpdateContents()
    {
        var general = _settings.Current.General;
        _label!.Text = _time.GetLocalNow().ToString("t", CultureInfo.CurrentCulture) + Environment.NewLine + _time.GetLocalNow().ToString("ddd, d MMM", CultureInfo.CurrentCulture);
        _label.Visibility = general.DesktopClock ? Visibility.Visible : Visibility.Collapsed;
        _media!.Visibility = general.DesktopNowPlaying || general.DesktopVisualizer ? Visibility.Visible : Visibility.Collapsed;
        _label.Foreground = _media.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.White;
        if (SystemParameters.HighContrast)
        {
            ((Border)_window!.Content).Background = SystemColors.WindowBrush;
        }
        else if (((Border)_window!.Content).Background == SystemColors.WindowBrush)
        {
            ((Border)_window.Content).Background = new SolidColorBrush(Color.FromArgb(220, 24, 24, 24));
        }
        if (general.DesktopNowPlaying && _time.GetUtcNow() >= _nextMediaRead && !_readingMedia)
        {
            _nextMediaRead = _time.GetUtcNow() + TimeSpan.FromSeconds(5);
            _ = UpdateMediaAsync();
        }

        var animate = general.DesktopVisualizer && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        if (animate && _time.GetUtcNow() >= _nextDeviceCheck)
        {
            _nextDeviceCheck = _time.GetUtcNow() + TimeSpan.FromSeconds(5);
            _visualizer.StartOrRefreshDevice();
        }
        else if (!animate)
        {
            _visualizer.Stop();
        }

        if (general.DesktopVisualizer && _visualizer.HasError)
        {
            _media.Text = Strings.Widget_PlaybackUnavailable;
        }
        else if (general.DesktopVisualizer && !animate && !general.DesktopNowPlaying)
        {
            _media.Text = Strings.Widget_VisualizerPaused;
        }
        else if (general.DesktopVisualizer && !general.DesktopNowPlaying)
        {
            _media.Text = Strings.Widget_DefaultOutput;
        }

        var levels = _visualizer.Levels;
        for (var i = 0; i < _bars.Count; i++)
        {
            _bars[i].Visibility = animate ? Visibility.Visible : Visibility.Collapsed;
            _bars[i].Height = 2 + (40 * levels[i]);
        }
    }

    private async Task UpdateMediaAsync()
    {
        _readingMedia = true;
        try
        {
            var media = await _nowPlaying.ReadAsync();
            if (!_disposed && _settings.Current.General.DesktopNowPlaying && _media is not null)
            {
                _media.Text = media.State switch
                {
                    MediaSessionState.Empty => Strings.Widget_NothingPlaying,
                    MediaSessionState.Unavailable => Strings.Widget_MediaUnavailable,
                    _ => media.Title + (string.IsNullOrWhiteSpace(media.Artist) ? "" : " · " + media.Artist),
                };
            }
        }
        finally
        {
            _readingMedia = false;
        }
    }
}
