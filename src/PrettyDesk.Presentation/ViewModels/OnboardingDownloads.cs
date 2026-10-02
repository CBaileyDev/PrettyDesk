using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly HashSet<string> _startedDownloads = new(StringComparer.Ordinal);
    private bool _disposed;
    [ObservableProperty] private bool _downloadInBackground;
    [ObservableProperty] private string _displaySummary = string.Empty;
    [ObservableProperty] private string _downloadSummary = string.Empty;
    public ObservableCollection<OnboardingPackViewModel> WallpaperChoices { get; } = [];
    public bool CanAdvance => !IsBusy && !IsLoadingGames;
    public bool HasWallpaperChoices => WallpaperChoices.Count > 0;
    public bool HasDownloadSelection => WallpaperChoices.Any(p => p.IsSelected && p.Plan.HasWallpapers && p.Plan.CanDownload && p.Plan.MissingBytes > 0 && !p.IsDownloading);

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

        RefreshDownloadPlans();
        OnPropertyChanged(nameof(HasWallpaperChoices));
    }

    private void RefreshDownloadPlans()
    {
        var monitors = _monitors.GetMonitors().Where(m => m.PixelWidth > 0 && m.PixelHeight > 0).ToList();
        DisplaySummary = monitors.Count == 0 ? Strings.Onboard_DisplaysMissing : string.Join(" · ", monitors
            .GroupBy(m => (m.PixelWidth, m.PixelHeight))
            .Select(g => Strings.Format(Strings.Onboard_DisplayGroup, g.Count(), g.Key.PixelWidth, g.Key.PixelHeight,
                FormatName(VariantSelector.Select(g.Key.PixelWidth, g.Key.PixelHeight, Variants.All.ToDictionary(v => v.Key, v => v.Ratio))!.Key))));
        foreach (var row in WallpaperChoices)
        {
            row.Plan = _content.GetDownloadPlan(row.PackId, monitors);
            var state = _content.GetPackState(row.PackId);
            row.CanSelect = row.Plan.HasWallpapers && (row.Plan.CanDownload || row.Plan.MissingBytes == 0);
            row.IsDownloading = state.State == PackStateKind.Downloading;
            row.Progress = Math.Clamp(state.Fraction, 0, 1) * 100;
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

        UpdateDownloadSummary();
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

    private void UpdateDownloadSummary()
    {
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

    [RelayCommand(CanExecute = nameof(HasDownloadSelection))]
    private void DownloadSelected() => RequestSelectedDownloads(retry: true);

    private void RequestSelectedDownloads(bool retry)
    {
        var monitors = _monitors.GetMonitors();
        if (monitors.Count == 0) { return; }
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
    private void OnDisplayPlanChanged() => _ui.Post(() => { if (!_disposed) { RefreshDownloadPlans(); } });

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _content.ProgressChanged -= OnDownloadProgress;
            _content.PackChanged -= OnPackChanged;
            _catalog.Changed -= OnDisplayPlanChanged;
            _monitors.Changed -= OnDisplayPlanChanged;
            foreach (var row in WallpaperChoices) { row.PropertyChanged -= OnDownloadChoiceChanged; }
        }

        base.Dispose(disposing);
    }
}
