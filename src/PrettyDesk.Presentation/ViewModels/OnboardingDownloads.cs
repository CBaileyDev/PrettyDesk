using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Imaging;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.ViewModels;

public sealed partial class OnboardingPackViewModel(string gameId, string packId, string name) : ObservableObject
{
    public string GameId { get; } = gameId;
    public string PackId { get; } = packId;
    public string Name { get; } = name;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _canSelect;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isDownloading;
    public PackDownloadPlan Plan { get; internal set; } = new(false, false, 0, [], false);
}

public sealed partial class OnboardingViewModel
{
    private readonly IUiDispatcher _ui;
    private readonly CancellationTokenSource _displayPlanCancellation = new();
    private readonly CancellationToken _displayPlanToken;
    private readonly HashSet<string> _startedDownloads = new(StringComparer.Ordinal);
    private List<MonitorInfo> _availableMonitors = [];
    private bool _hasMonitorSnapshot;
    private bool _displayRefreshFailed;
    private int _displayRefreshVersion;
    private bool _disposed;
    [ObservableProperty] private bool _downloadInBackground;
    [ObservableProperty] private string _displaySummary = string.Empty;
    [ObservableProperty] private string _downloadSummary = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadSelection))]
    private bool _isLoadingDisplayPlans;
    public ObservableCollection<OnboardingPackViewModel> WallpaperChoices { get; } = [];
    public bool CanAdvance => !IsBusy && !IsLoadingGames;
    public bool HasWallpaperChoices => WallpaperChoices.Count > 0;
    public bool HasDownloadSelection => _hasMonitorSnapshot && _availableMonitors.Count > 0 && !IsLoadingDisplayPlans && !_displayRefreshFailed
        && WallpaperChoices.Any(p => p.IsSelected && p.Plan.HasWallpapers && p.Plan.CanDownload && p.Plan.MissingBytes > 0 && !p.IsDownloading);

    private void BuildWallpaperChoices()
    {
        var previous = WallpaperChoices.ToDictionary(p => p.GameId, p => p.IsSelected);
        foreach (var row in WallpaperChoices) { row.PropertyChanged -= OnDownloadChoiceChanged; }
        WallpaperChoices.Clear();
        foreach (var game in Games.Where(g => g.IsEnabled))
        {
            var packId = _catalog.Current.FindGame(game.Id)?.PackId;
            if (string.IsNullOrEmpty(packId)) { continue; }
            var row = new OnboardingPackViewModel(game.Id, packId, game.Name)
            {
                IsSelected = previous.GetValueOrDefault(game.Id, !_settings.Current.Games.TryGetValue(game.Id, out var preference) || preference.PrefetchWallpapers),
            };
            row.PropertyChanged += OnDownloadChoiceChanged;
            WallpaperChoices.Add(row);
        }

        RequestDisplayPlanRefresh();
        OnPropertyChanged(nameof(HasWallpaperChoices));
    }

    private void RefreshDownloadPlans(bool finalizingDisplayRefresh = false)
    {
        var monitors = _availableMonitors;
        DisplaySummary = IsLoadingDisplayPlans && !finalizingDisplayRefresh
            ? _hasMonitorSnapshot ? Strings.Onboard_UpdatingDisplays : Strings.Onboard_DetectingDisplays
            : _displayRefreshFailed
                ? Strings.Onboard_DisplaysUnavailable
                : !_hasMonitorSnapshot
                    ? Strings.Onboard_DisplaysMissing
                : monitors.Count == 0 ? Strings.Onboard_DisplaysMissing : string.Join(" · ", monitors
            .GroupBy(m => (m.PixelWidth, m.PixelHeight))
            .Select(g => Strings.Format(Strings.Onboard_DisplayGroup, g.Count(), g.Key.PixelWidth, g.Key.PixelHeight,
                FormatName(VariantSelector.Select(g.Key.PixelWidth, g.Key.PixelHeight, Variants.All.ToDictionary(v => v.Key, v => v.Ratio))!.Key))));
        foreach (var row in WallpaperChoices)
        {
            var state = _content.GetPackState(row.PackId);
            row.IsDownloading = state.State == PackStateKind.Downloading;
            row.Progress = Math.Clamp(state.Fraction, 0, 1) * 100;
            if (IsLoadingDisplayPlans && !finalizingDisplayRefresh)
            {
                row.CanSelect = false;
                row.Status = _hasMonitorSnapshot ? Strings.Onboard_UpdatingDisplays : Strings.Onboard_DetectingDisplays;
                continue;
            }

            if (!_hasMonitorSnapshot || monitors.Count == 0 || _displayRefreshFailed)
            {
                row.Plan = new PackDownloadPlan(false, false, 0, [], false);
                row.CanSelect = false;
                row.Status = IsLoadingDisplayPlans ? Strings.Onboard_DetectingDisplays
                    : _displayRefreshFailed ? Strings.Onboard_DisplaysUnavailable : Strings.Onboard_DisplaysMissing;
                continue;
            }

            row.Plan = _content.GetDownloadPlan(row.PackId, monitors);
            row.CanSelect = monitors.Count > 0 && row.Plan.HasWallpapers && (row.Plan.CanDownload || row.Plan.MissingBytes == 0);
            row.Status = !row.Plan.HasWallpapers ? Strings.Onboard_NoMatchingArt
                : row.Plan.MissingBytes == 0 ? Strings.Onboard_AlreadyIncluded
                : !row.Plan.CanDownload ? Strings.Onboard_DownloadUnavailable
                : row.IsDownloading ? Strings.Format(Strings.Onboard_DownloadProgress, (int)row.Progress)
                : state.State == PackStateKind.Failed ? Strings.Onboard_DownloadRetry
                : Strings.Format(Strings.Onboard_PackDownloadSize, SizeLabel(row.Plan.MissingBytes), string.Join(", ", row.Plan.VariantKeys.Select(FormatName)))
                    + (row.Plan.UsesFallback ? " " + Strings.Onboard_FormatFallback : string.Empty);
            // A transient display disconnect must not discard the user's future download preference.
            if (!row.CanSelect && monitors.Count > 0) { row.IsSelected = false; }
        }

        UpdateDownloadSummary(finalizingDisplayRefresh);
    }

    private static string FormatName(string key) => key switch
    {
        "16x9" => Strings.Onboard_FormatLandscape,
        "21x9" => Strings.Onboard_FormatUltrawide,
        "9x16" => Strings.Onboard_FormatPortrait,
        "32x9" => Strings.Onboard_FormatSuperwide,
        _ => key.Replace('x', ':'),
    };

    private static string SizeLabel(long bytes) => Strings.Format(Strings.Onboard_Megabytes, Math.Max(0.1, bytes / 1048576d).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture));

    private void UpdateDownloadSummary(bool finalizingDisplayRefresh = false)
    {
        if (IsLoadingDisplayPlans && !finalizingDisplayRefresh)
        {
            DownloadSummary = _hasMonitorSnapshot ? Strings.Onboard_UpdatingDisplays : Strings.Onboard_DetectingDisplays;
            OnPropertyChanged(nameof(HasDownloadSelection));
            DownloadSelectedCommand.NotifyCanExecuteChanged();
            return;
        }

        if (_displayRefreshFailed)
        {
            DownloadSummary = Strings.Onboard_DisplaysUnavailable;
            OnPropertyChanged(nameof(HasDownloadSelection));
            DownloadSelectedCommand.NotifyCanExecuteChanged();
            return;
        }

        if (!_hasMonitorSnapshot || _availableMonitors.Count == 0)
        {
            DownloadSummary = Strings.Onboard_DisplaysMissing;
            OnPropertyChanged(nameof(HasDownloadSelection));
            DownloadSelectedCommand.NotifyCanExecuteChanged();
            return;
        }

        var selected = WallpaperChoices.Where(p => p.IsSelected && p.Plan.MissingBytes > 0 && p.Plan.CanDownload).DistinctBy(p => p.PackId).ToList();
        DownloadSummary = selected.Count == 0 ? Strings.Onboard_NoDownloadsNeeded
            : Strings.Format(Strings.Onboard_DownloadTotal, selected.Count, SizeLabel(selected.Sum(p => p.Plan.MissingBytes)));
        OnPropertyChanged(nameof(HasDownloadSelection));
        DownloadSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnDownloadChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OnboardingPackViewModel.IsSelected)) { UpdateDownloadSummary(); }
    }

    partial void OnIsLoadingDisplayPlansChanged(bool value) => DownloadSelectedCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(HasDownloadSelection))]
    private void DownloadSelected() => RequestSelectedDownloads(retry: true);

    private void RequestSelectedDownloads(bool retry)
    {
        if (!HasDownloadSelection) { return; }
        var monitors = _availableMonitors;
        foreach (var row in WallpaperChoices.Where(p => p.IsSelected).DistinctBy(p => p.PackId))
        {
            var plan = _content.GetDownloadPlan(row.PackId, monitors);
            if (!plan.HasWallpapers || !plan.CanDownload || plan.MissingBytes == 0 || row.IsDownloading || (!retry && _startedDownloads.Contains(row.PackId))) { continue; }
            _startedDownloads.Add(row.PackId);
            row.IsDownloading = true;
            row.Status = Strings.Format(Strings.Onboard_DownloadProgress, 0);
            if (retry) { _content.RetryPack(row.PackId, monitors); }
            else { _content.RequestPack(row.PackId, monitors); }
        }

        UpdateDownloadSummary();
    }

    private void OnDownloadProgress(PackProgress progress) => OnPackChanged(progress.PackId);
    private void OnPackChanged(string packId) => _ui.Post(() => { if (!_disposed && WallpaperChoices.Any(p => p.PackId == packId)) { RefreshDownloadPlans(); } });
    private void OnCatalogChanged() => _ui.Post(() => { if (!_disposed) { RefreshDownloadPlans(); } });

    private void OnMonitorsChanged() => _ui.Post(() => { if (!_disposed) { RequestDisplayPlanRefresh(); } });

    private void RequestDisplayPlanRefresh()
    {
        var version = Interlocked.Increment(ref _displayRefreshVersion);
        _displayRefreshFailed = false;
        IsLoadingDisplayPlans = true;
        DisplaySummary = _hasMonitorSnapshot ? Strings.Onboard_UpdatingDisplays : Strings.Onboard_DetectingDisplays;
        RefreshDownloadPlans();
        _ = RefreshDisplayPlansAsync(version, _displayPlanToken);
    }

    private async Task RefreshDisplayPlansAsync(int version, CancellationToken cancellationToken)
    {
        IReadOnlyList<MonitorInfo> monitors;
        try
        {
            monitors = await Task.Run(_monitors.GetMonitors, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
#pragma warning disable CA1031 // A display-provider failure is surfaced in the onboarding step instead of becoming an unobserved task.
        catch (Exception)
#pragma warning restore CA1031
        {
            _ui.Post(() =>
            {
                if (_disposed || version != Volatile.Read(ref _displayRefreshVersion)) { return; }
                _displayRefreshFailed = true;
                IsLoadingDisplayPlans = false;
                RefreshDownloadPlans(finalizingDisplayRefresh: true);
            });
            return;
        }

        _ui.Post(() =>
        {
            if (_disposed || version != Volatile.Read(ref _displayRefreshVersion)) { return; }
            _availableMonitors = monitors.Where(m => m.PixelWidth > 0 && m.PixelHeight > 0).ToList();
            _hasMonitorSnapshot = true;
            _displayRefreshFailed = false;
            IsLoadingDisplayPlans = false;
            RefreshDownloadPlans(finalizingDisplayRefresh: true);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _content.ProgressChanged -= OnDownloadProgress;
            _content.PackChanged -= OnPackChanged;
            _catalog.Changed -= OnCatalogChanged;
            _monitors.Changed -= OnMonitorsChanged;
            Interlocked.Increment(ref _displayRefreshVersion);
            _displayPlanCancellation.Cancel();
            _displayPlanCancellation.Dispose();
            foreach (var row in WallpaperChoices) { row.PropertyChanged -= OnDownloadChoiceChanged; }
        }

        base.Dispose(disposing);
    }
}
