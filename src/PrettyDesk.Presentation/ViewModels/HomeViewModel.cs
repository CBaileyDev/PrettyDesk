using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Presentation.Formatting;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.ViewModels;

public enum BannerSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record BannerViewModel(EnvironmentConflict Kind, BannerSeverity Severity, string Title, string Message);

/// <summary>One display drawn to scale in a fixed 1000-unit-wide canvas (the view wraps it in a Viewbox).</summary>
public sealed class MonitorPreviewViewModel : ObservableObject
{
    private string? _thumbnailPath;

    public MonitorPreviewViewModel(string id, double x, double y, double width, double height, string label, string? thumbnailPath)
    {
        Id = id;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Label = label;
        _thumbnailPath = thumbnailPath;
    }

    public string Id { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public string Label { get; }

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            if (SetProperty(ref _thumbnailPath, value))
            {
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }
    }

    public bool HasThumbnail => !string.IsNullOrEmpty(_thumbnailPath);
}

/// <summary>Home: a to-scale preview of every display, the status card, quick actions and environment banners (SPEC §7.1).</summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    public const double CanvasWidth = 1000;
    private const double Gap = 24;

    private readonly IWallpaperController _controller;
    private readonly IMonitorProvider _monitors;
    private readonly IContentBrowser _content;
    private readonly IEnvironmentConflictSource _conflicts;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private int _monitorRefreshVersion;
    private int _disposed;

    [ObservableProperty]
    private string _headline = Strings.Status_Starting;

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    [NotifyPropertyChangedFor(nameof(CanSkip))]
    private OrchestratorMode _mode = OrchestratorMode.Default;

    [ObservableProperty]
    private double _canvasHeight = 300;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoMonitors))]
    private bool _isLoadingMonitors = true;

    public HomeViewModel(
        IWallpaperController controller,
        IMonitorProvider monitors,
        IContentBrowser content,
        IEnvironmentConflictSource conflicts,
        IUiDispatcher ui,
        TimeProvider time)
    {
        _controller = controller;
        _monitors = monitors;
        _content = content;
        _conflicts = conflicts;
        _ui = ui;
        _time = time;

        _controller.StatusChanged += OnStatusChanged;
        _monitors.Changed += OnMonitorsChanged;
        _content.PackChanged += OnContentChanged;
        ApplyStatus(_controller.Status);
        BuildBanners();
        RequestMonitorRefresh();
    }

    public ObservableCollection<MonitorPreviewViewModel> Monitors { get; } = [];

    public ObservableCollection<BannerViewModel> Banners { get; } = [];

    public bool HasMonitors => Monitors.Count > 0;

    public bool ShowNoMonitors => !HasMonitors && !IsLoadingMonitors && string.IsNullOrEmpty(ErrorMessage);

    public bool CanPause => Mode is not (OrchestratorMode.Paused or OrchestratorMode.Blocked);

    public bool CanResume => Mode == OrchestratorMode.Paused;

    public bool CanSkip => Mode is OrchestratorMode.Default or OrchestratorMode.Game or OrchestratorMode.GameGrace;

    /// <summary>Re-reads status, displays and environment conflicts. Safe to call from any thread.</summary>
    public void Refresh() => _ui.Post(() =>
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var status = _controller.Status;
        ApplyStatus(status);
        UpdateThumbnails(status);
        BuildBanners();
        RequestMonitorRefresh();
    });

    [RelayCommand(CanExecute = nameof(CanSkip))]
    private void Next() => _controller.NextWallpaper();

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void PauseForOneHour() => _controller.Pause(TimeSpan.FromHours(1));

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void PauseUntilResumed() => _controller.Pause(null);

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume() => _controller.Resume();

    [RelayCommand]
    private void DismissBanner(BannerViewModel? banner)
    {
        if (banner is not null)
        {
            Banners.Remove(banner);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Exchange(ref _disposed, 1);
            Interlocked.Increment(ref _monitorRefreshVersion);
            _controller.StatusChanged -= OnStatusChanged;
            _monitors.Changed -= OnMonitorsChanged;
            _content.PackChanged -= OnContentChanged;
        }

        base.Dispose(disposing);
    }

    partial void OnModeChanged(OrchestratorMode value)
    {
        NextCommand.NotifyCanExecuteChanged();
        PauseForOneHourCommand.NotifyCanExecuteChanged();
        PauseUntilResumedCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
    }

    private void OnStatusChanged(OrchestratorStatus status) => _ui.Post(() =>
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        ApplyStatus(status);
        UpdateThumbnails(status);
        BuildBanners();
    });

    private void OnMonitorsChanged()
        => RequestMonitorRefresh();

    private void RequestMonitorRefresh()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var version = Interlocked.Increment(ref _monitorRefreshVersion);
        _ui.Post(() =>
        {
            if (Volatile.Read(ref _disposed) == 0 && version == Volatile.Read(ref _monitorRefreshVersion))
            {
                IsLoadingMonitors = true;
                ErrorMessage = null;
                OnPropertyChanged(nameof(ShowNoMonitors));
            }
        });
        _ = RefreshMonitorsAsync(version);
    }

    private async Task RefreshMonitorsAsync(int version)
    {
        IReadOnlyList<MonitorInfo> monitors;
        try
        {
            // Display notifications can arrive on any thread, and the provider may wait for COM.
            monitors = await Task.Run(_monitors.GetMonitors).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A display-provider failure must not become an unobserved task from a system event.
        catch (Exception)
#pragma warning restore CA1031
        {
            _ui.Post(() =>
            {
                if (Volatile.Read(ref _disposed) == 0 && version == Volatile.Read(ref _monitorRefreshVersion))
                {
                    ErrorMessage = Strings.Home_DisplaysUnavailable;
                    IsLoadingMonitors = false;
                    OnPropertyChanged(nameof(ShowNoMonitors));
                }
            });
            return;
        }

        _ui.Post(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || version != Volatile.Read(ref _monitorRefreshVersion))
            {
                return;
            }

            ErrorMessage = null;
            BuildMonitors(monitors);
            BuildBanners();
            IsLoadingMonitors = false;
        });
    }

    private void OnContentChanged(string packId) => _ui.Post(() =>
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            UpdateThumbnails(_controller.Status);
        }
    });

    private void ApplyStatus(OrchestratorStatus status)
    {
        var text = StatusFormatter.Describe(status, _time.GetUtcNow());
        Headline = text.Headline;
        Detail = text.Detail;
        Mode = status.Mode;
    }

    private void BuildMonitors(IReadOnlyList<MonitorInfo> list)
    {
        Monitors.Clear();
        if (list.Count == 0)
        {
            CanvasHeight = 300;
            OnPropertyChanged(nameof(HasMonitors));
            OnPropertyChanged(nameof(ShowNoMonitors));
            return;
        }

        var minX = list.Min(m => m.Left);
        var minY = list.Min(m => m.Top);
        var maxX = list.Max(m => m.Left + m.PixelWidth);
        var maxY = list.Max(m => m.Top + m.PixelHeight);
        var scale = CanvasWidth / Math.Max(1, maxX - minX);
        CanvasHeight = Math.Max(120, ((maxY - minY) * scale) + (Gap * 2));

        var status = _controller.Status;
        var ordered = list.OrderBy(m => m.Left).ThenBy(m => m.Top).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var m = ordered[i];
            status.WallpaperByMonitor.TryGetValue(m.Id, out var wallpaperId);
            var label = Strings.Format(m.IsPrimary ? Strings.Home_PrimaryDisplay : Strings.Home_DisplayLabel, i + 1, m.PixelWidth, m.PixelHeight);
            Monitors.Add(new MonitorPreviewViewModel(
                m.Id,
                (m.Left - minX) * scale,
                ((m.Top - minY) * scale) + Gap,
                m.PixelWidth * scale,
                m.PixelHeight * scale,
                label,
                wallpaperId is null ? null : _content.GetPreviewImagePath(wallpaperId)));
        }

        OnPropertyChanged(nameof(HasMonitors));
        OnPropertyChanged(nameof(ShowNoMonitors));
    }

    private void UpdateThumbnails(OrchestratorStatus status)
    {
        foreach (var monitor in Monitors)
        {
            monitor.ThumbnailPath = status.WallpaperByMonitor.TryGetValue(monitor.Id, out var id) ? _content.GetPreviewImagePath(id) : monitor.ThumbnailPath;
        }
    }

    private void BuildBanners()
    {
        var conflicts = _conflicts.Detect();
        Banners.Clear();
        if (conflicts.HasFlag(EnvironmentConflict.PolicyLocked))
        {
            Banners.Add(new BannerViewModel(EnvironmentConflict.PolicyLocked, BannerSeverity.Error, Strings.Banner_PolicyTitle, Strings.Banner_PolicyBody));
        }

        if (conflicts.HasFlag(EnvironmentConflict.WallpaperEngine))
        {
            Banners.Add(new BannerViewModel(EnvironmentConflict.WallpaperEngine, BannerSeverity.Warning, Strings.Banner_WallpaperEngineTitle, Strings.Banner_WallpaperEngineBody));
        }

        if (conflicts.HasFlag(EnvironmentConflict.Lively))
        {
            Banners.Add(new BannerViewModel(EnvironmentConflict.Lively, BannerSeverity.Warning, Strings.Banner_LivelyTitle, Strings.Banner_LivelyBody));
        }

        if (conflicts.HasFlag(EnvironmentConflict.SpotlightOrSlideshow))
        {
            Banners.Add(new BannerViewModel(EnvironmentConflict.SpotlightOrSlideshow, BannerSeverity.Info, Strings.Banner_SpotlightTitle, Strings.Banner_SpotlightBody));
        }
    }
}
