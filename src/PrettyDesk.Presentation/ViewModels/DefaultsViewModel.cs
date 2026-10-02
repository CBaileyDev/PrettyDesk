using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.ViewModels;

public sealed partial class CollectionCardViewModel : ObservableObject
{
    private bool _quiet;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string? _previewPath;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public CollectionCardViewModel(CollectionEntry collection, int wallpaperCount)
    {
        Id = collection.Id;
        Title = collection.Title;
        Count = wallpaperCount;
        Tone = collection.Tone;
    }

    public string Id { get; }

    public string Title { get; }

    public int Count { get; }

    public string Tone { get; }

    public string ToggleLabel => Strings.Format(Strings.Defaults_CollectionToggle, Title);

    public string CountText => Strings.Format(Strings.Defaults_CollectionCount, Count);

    public Action<CollectionCardViewModel>? SelectedChanged { get; set; }

    public void SetSelectedQuietly(bool value)
    {
        _quiet = true;
        try
        {
            IsSelected = value;
        }
        finally
        {
            _quiet = false;
        }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_quiet)
        {
            SelectedChanged?.Invoke(this);
        }
    }
}

public sealed record MyImageViewModel(string Id, string Name, string Path, string? PreviewPath, string? Warning)
{
    public string RemoveLabel => Strings.Format(Strings.Defaults_RemoveImage, Name);
}

/// <summary>Defaults page (SPEC §7.3): collections, "My images", mode, interval, order and theme options.</summary>
public sealed partial class DefaultsViewModel : SettingsSectionViewModel
{
    private sealed class MonitorRefreshQuery
    {
        private readonly TaskCompletionSource<List<MonitorInfo>> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _superseded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<List<MonitorInfo>> Task => _completion.Task;

        public Task Superseded => _superseded.Task;

        public void Complete(List<MonitorInfo> monitors) => _completion.TrySetResult(monitors);

        public void MarkSuperseded() => _superseded.TrySetResult();
    }

    public const int MinCustomMinutes = 1;
    public const int MaxCustomMinutes = 7 * 24 * 60;

    private readonly ICatalogProvider _catalog;
    private readonly IContentBrowser _content;
    private readonly IContentLibrary _library;
    private readonly IMonitorProvider _monitors;
    private readonly IFilePicker _files;
    private readonly IAppController _app;
    private readonly IUiDispatcher _ui;
    private readonly CancellationTokenSource _monitorRefreshCancellation = new();
    private readonly CancellationToken _monitorRefreshToken;
    private readonly IntervalChoice _customChoice;
    private List<MonitorInfo> _availableMonitors = [];
    private MonitorRefreshQuery? _monitorRefreshQuery;

    [ObservableProperty]
    private bool _isRotate = true;

    [ObservableProperty]
    private bool _isShuffle = true;

    [ObservableProperty]
    private bool _useMyImages;

    [ObservableProperty]
    private bool _followTheme;

    [ObservableProperty]
    private bool _pauseOnBatterySaver = true;

    [ObservableProperty]
    private IntervalChoice? _selectedInterval;

    [ObservableProperty]
    private int _customMinutes = 30;

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private string? _importMessage;

    [ObservableProperty]
    private bool _isLoadingMonitors = true;

    [ObservableProperty]
    private string? _displayGuidanceStatus;

    public DefaultsViewModel(
        ISettingsProvider settings,
        ICatalogProvider catalog,
        IContentBrowser content,
        IContentLibrary library,
        IMonitorProvider monitors,
        IFilePicker files,
        IAppController app,
        IUiDispatcher ui)
        : base(settings, ui)
    {
        _catalog = catalog;
        _content = content;
        _library = library;
        _monitors = monitors;
        _files = files;
        _app = app;
        _ui = ui;
        _monitorRefreshToken = _monitorRefreshCancellation.Token;

        _customChoice = new IntervalChoice(RotationInterval.Every(TimeSpan.FromMinutes(30)), Strings.Interval_Custom);
        IntervalChoices = [.. IntervalLabels.Presets(includeSession: false), _customChoice];

        _catalog.Changed += OnModelChanged;
        _content.PackChanged += OnPackChanged;
        _monitors.Changed += OnMonitorsChanged;
        InitialLoad();
        RequestMonitorRefresh();
    }

    public IReadOnlyList<IntervalChoice> IntervalChoices { get; }

    public ObservableCollection<CollectionCardViewModel> Collections { get; } = [];

    public ObservableCollection<MyImageViewModel> MyImages { get; } = [];

    public ObservableCollection<WallpaperItemViewModel> FixedCandidates { get; } = [];

    public bool HasCollections => Collections.Count > 0;

    public bool HasImages => MyImages.Count > 0;

    public bool IsCustomInterval => ReferenceEquals(SelectedInterval, _customChoice);

    public bool ShowRotationOptions => IsRotate;

    public bool ShowFixedPicker => !IsRotate;

    public bool HasFixedCandidates => FixedCandidates.Count > 0;

    protected override void Load(AppSettings settings)
    {
        var d = settings.Default;
        IsRotate = d.Mode == WallpaperMode.Rotate;
        IsShuffle = d.Order == RotationOrder.Shuffle;
        FollowTheme = d.FollowWindowsTheme;
        PauseOnBatterySaver = d.PauseRotationOnBatterySaver;
        UseMyImages = d.Selection.Collections.Contains(ContentIds.UserPackId);

        var preset = IntervalChoices.FirstOrDefault(c => !ReferenceEquals(c, _customChoice) && c.Value == d.Interval);
        if (IsCustomInterval && d.Interval.Kind == RotationIntervalKind.Duration && (int)d.Interval.Duration.TotalMinutes == CustomMinutes)
        {
            // The user chose "Custom…" and its value happens to equal a preset: keep Custom selected so the minutes box stays.
        }
        else if (preset is null && d.Interval.Kind == RotationIntervalKind.Duration)
        {
            CustomMinutes = (int)d.Interval.Duration.TotalMinutes;
            SelectedInterval = _customChoice;
        }
        else
        {
            SelectedInterval = preset ?? IntervalChoices[2];
        }

        BuildCollections(settings);
        BuildImages();
        BuildFixedCandidates(settings);
        OnPropertyChanged(nameof(IsCustomInterval));
    }

    partial void OnIsRotateChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRotationOptions));
        OnPropertyChanged(nameof(ShowFixedPicker));
        Save(s =>
        {
            s.Default.Mode = value ? WallpaperMode.Rotate : WallpaperMode.Fixed;
            if (!value && s.Default.FixedWallpaperId is null)
            {
                s.Default.FixedWallpaperId = FixedCandidates.FirstOrDefault()?.Id;
            }
        });
        if (!value && FixedCandidates.Count == 0)
        {
            Notice = Strings.Defaults_NoFixedYet;
        }
    }

    partial void OnIsShuffleChanged(bool value) => Save(s => s.Default.Order = value ? RotationOrder.Shuffle : RotationOrder.Sequential);

    partial void OnFollowThemeChanged(bool value) => Save(s => s.Default.FollowWindowsTheme = value);

    partial void OnPauseOnBatterySaverChanged(bool value) => Save(s => s.Default.PauseRotationOnBatterySaver = value);

    partial void OnSelectedIntervalChanged(IntervalChoice? value)
    {
        OnPropertyChanged(nameof(IsCustomInterval));
        if (value is null)
        {
            return;
        }

        var interval = ReferenceEquals(value, _customChoice) ? RotationInterval.Every(TimeSpan.FromMinutes(Math.Clamp(CustomMinutes, MinCustomMinutes, MaxCustomMinutes))) : value.Value;
        Save(s => s.Default.Interval = interval);
    }

    partial void OnCustomMinutesChanged(int value)
    {
        var clamped = Math.Clamp(value, MinCustomMinutes, MaxCustomMinutes);
        if (clamped != value)
        {
            CustomMinutes = clamped;
            return;
        }

        if (IsCustomInterval)
        {
            Save(s => s.Default.Interval = RotationInterval.Every(TimeSpan.FromMinutes(clamped)));
        }
    }

    partial void OnUseMyImagesChanged(bool value)
    {
        if (value == Settings.Current.Default.Selection.Collections.Contains(ContentIds.UserPackId))
        {
            return;
        }

        if (!value && !WouldHaveSelection(without: ContentIds.UserPackId))
        {
            Reload(keepNotice: Strings.Defaults_KeepOne);
            return;
        }

        Notice = null;
        Save(s =>
        {
            var collections = s.Default.Selection.Collections;
            if (value && !collections.Contains(ContentIds.UserPackId))
            {
                collections.Add(ContentIds.UserPackId);
            }
            else if (!value)
            {
                collections.Remove(ContentIds.UserPackId);
            }
        });
    }

    [RelayCommand]
    private async Task AddImagesAsync() => await ImportAsync(_files.PickImages());

    /// <summary>Imports images chosen with the picker or dropped on the page (FR-CON-7). Public so the view can forward drops.</summary>
    public async Task ImportAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var results = await Task.Run(() => paths.Select(_content.ImportUserImage).ToList());
            var monitors = await GetCurrentMonitorSnapshotAsync();
            if (IsDisposed)
            {
                return;
            }

            var added = results.Where(r => r.Ok).Select(r => r.Image!).ToList();
            var skipped = results.Where(r => !r.Ok).Select(r => r.Message).Where(m => m is not null).ToList();
            var warnings = added
                .Select(i => UserImageStore.ResolutionWarning(i, monitors))
                .Where(w => w is not null)
                .ToList();

            ImportMessage = string.Join(' ', new[]
            {
                added.Count > 0 ? Strings.Format(Strings.Defaults_ImportDone, added.Count) : null,
                skipped.Count > 0 ? Strings.Format(Strings.Defaults_ImportSkipped, skipped.Count, skipped[0]) : null,
                warnings.FirstOrDefault(),
            }.Where(m => m is not null));

            if (added.Count > 0)
            {
                UseMyImages = true;
            }

            Load(Settings.Current);
        }, Strings.Defaults_ImportFailed);
    }

    [RelayCommand]
    private void RemoveImage(MyImageViewModel? image)
    {
        if (image is null)
        {
            return;
        }

        _content.RemoveUserImage(image.Id);

        // The image may also be someone's fixed wallpaper; clear that so nothing points at a missing file.
        Save(s =>
        {
            if (s.Default.FixedWallpaperId == image.Id)
            {
                s.Default.FixedWallpaperId = null;
            }
        });
        Load(Settings.Current);
    }

    [RelayCommand]
    private void RerunSetup() => _app.ShowOnboarding();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            _catalog.Changed -= OnModelChanged;
            _content.PackChanged -= OnPackChanged;
            _monitors.Changed -= OnMonitorsChanged;
            Volatile.Read(ref _monitorRefreshQuery)?.MarkSuperseded();
            _monitorRefreshCancellation.Cancel();
            _monitorRefreshCancellation.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnModelChanged() => _ui.Post(() => Load(Settings.Current));

    private void OnPackChanged(string packId) => _ui.Post(() => Load(Settings.Current));

    private void OnMonitorsChanged()
    {
        _ui.Post(() =>
        {
            if (!IsDisposed)
            {
                RequestMonitorRefresh();
            }
        });
    }

    private void RequestMonitorRefresh()
    {
        if (IsDisposed)
        {
            return;
        }

        IsLoadingMonitors = true;
        DisplayGuidanceStatus = null;
        var query = new MonitorRefreshQuery();
        Interlocked.Exchange(ref _monitorRefreshQuery, query)?.MarkSuperseded();
        _ = RefreshMonitorsAsync(query, _monitorRefreshToken);
    }

    internal async Task<List<MonitorInfo>> GetCurrentMonitorSnapshotAsync()
    {
        while (!IsDisposed)
        {
            var query = Volatile.Read(ref _monitorRefreshQuery);
            if (query is null)
            {
                return _availableMonitors;
            }

            await Task.WhenAny(query.Task, query.Superseded).ConfigureAwait(false);
            if (!query.Task.IsCompleted)
            {
                continue;
            }

            var monitors = await query.Task.ConfigureAwait(false);
            if (!IsDisposed && ReferenceEquals(query, Volatile.Read(ref _monitorRefreshQuery)))
            {
                return monitors;
            }
        }

        return _availableMonitors;
    }

    private async Task RefreshMonitorsAsync(MonitorRefreshQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<MonitorInfo> monitors;
        try
        {
            monitors = await Task.Run(_monitors.GetMonitors, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            query.Complete([]);
            return;
        }
#pragma warning disable CA1031 // An unavailable monitor list only suppresses optional resolution guidance.
        catch (Exception)
#pragma warning restore CA1031
        {
            _ui.Post(() =>
            {
                if (!IsDisposed && ReferenceEquals(query, Volatile.Read(ref _monitorRefreshQuery)))
                {
                    DisplayGuidanceStatus = Strings.Defaults_DisplaySizesUnavailable;
                    IsLoadingMonitors = false;
                }
            });
            query.Complete([]);
            return;
        }

        var filtered = monitors.Where(m => m.PixelWidth > 0 && m.PixelHeight > 0).ToList();
        _ui.Post(() =>
        {
            if (IsDisposed || !ReferenceEquals(query, Volatile.Read(ref _monitorRefreshQuery)))
            {
                return;
            }

            _availableMonitors = filtered;
            DisplayGuidanceStatus = filtered.Count == 0 ? Strings.Defaults_DisplaySizesUnavailable : null;
            BuildImages();
            IsLoadingMonitors = false;
        });
        query.Complete(filtered);
    }

    private void Reload(string? keepNotice)
    {
        Load(Settings.Current);
        Notice = keepNotice;
    }

    private bool WouldHaveSelection(string? without = null, string? withoutCollection = null)
    {
        var selection = Settings.Current.Default.Selection;
        var collections = selection.Collections.Where(c => c != without && c != withoutCollection).ToList();
        var hasImages = _content.ListUserImages().Count > 0;
        var usable = collections.Count(c => c != ContentIds.UserPackId || hasImages);
        return usable + selection.Wallpapers.Count > 0;
    }

    private void BuildCollections(AppSettings settings)
    {
        var selected = settings.Default.Selection.Collections.ToHashSet(StringComparer.Ordinal);
        var catalog = _catalog.Current;
        Collections.Clear();
        foreach (var collection in catalog.Collections.OrderBy(c => c.Order).ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var ids = _library.GetWallpaperIds(collection.PackId);
            var card = new CollectionCardViewModel(collection, ids.Count);
            card.SetSelectedQuietly(selected.Contains(collection.Id));
            var starter = catalog.FindPack(collection.PackId)?.Wallpapers.FirstOrDefault(w => w.Starter)?.Id ?? (ids.Count > 0 ? ids[0] : null);
            card.PreviewPath = starter is null ? null : _content.GetPreviewImagePath(starter);
            card.StatusText = _content.GetPackState(collection.PackId).State == PackStateKind.Ready ? Strings.Library_ChipReady : Strings.Defaults_CollectionNotDownloaded;
            card.SelectedChanged = OnCollectionToggled;
            Collections.Add(card);
        }

        OnPropertyChanged(nameof(HasCollections));
    }

    private void OnCollectionToggled(CollectionCardViewModel card)
    {
        if (!card.IsSelected && !WouldHaveSelection(withoutCollection: card.Id))
        {
            // Never leave the default with nothing to show.
            card.SetSelectedQuietly(true);
            Notice = Strings.Defaults_KeepOne;
            return;
        }

        Notice = null;
        Save(s =>
        {
            var collections = s.Default.Selection.Collections;
            if (card.IsSelected && !collections.Contains(card.Id))
            {
                collections.Add(card.Id);
            }
            else if (!card.IsSelected)
            {
                collections.Remove(card.Id);
            }
        });
        BuildFixedCandidates(Settings.Current);
    }

    private void BuildImages()
    {
        var monitors = _availableMonitors;
        MyImages.Clear();
        foreach (var image in _content.ListUserImages())
        {
            var name = image.Id["user:".Length..];
            var warning = UserImageStore.ResolutionWarning(image, monitors);
            MyImages.Add(new MyImageViewModel(image.Id, name, image.Path, image.Path, warning is null ? null : Strings.Format(Strings.Defaults_ImageWarning, name, warning)));
        }

        OnPropertyChanged(nameof(HasImages));
    }

    private void BuildFixedCandidates(AppSettings settings)
    {
        var catalog = _catalog.Current;
        var selection = settings.Default.Selection;
        var ids = new List<string>();
        foreach (var collectionId in selection.Collections)
        {
            var packId = catalog.Collections.FirstOrDefault(c => c.Id == collectionId)?.PackId ?? collectionId;
            ids.AddRange(_library.GetWallpaperIds(packId));
        }

        ids.AddRange(selection.Wallpapers);
        FixedCandidates.Clear();
        foreach (var id in ids.Distinct(StringComparer.Ordinal).Where(id => !selection.Excluded.Contains(id) && _library.TryGetAsset(id) is not null))
        {
            var item = new WallpaperItemViewModel(id, WallpaperTitles.Resolve(catalog, id)) { PreviewPath = _content.GetPreviewImagePath(id), IsAvailable = true };
            item.SetQuietly(true, settings.Default.Mode == WallpaperMode.Fixed && settings.Default.FixedWallpaperId == id);
            item.FavoriteChanged = OnFixedChosen;
            FixedCandidates.Add(item);
        }

        OnPropertyChanged(nameof(HasFixedCandidates));
    }

    private void OnFixedChosen(WallpaperItemViewModel item)
    {
        if (!item.IsFavorite)
        {
            // Unstarring the fixed wallpaper falls back to rotation rather than leaving "fixed" with nothing chosen.
            IsRotate = true;
            return;
        }

        foreach (var other in FixedCandidates.Where(w => w != item && w.IsFavorite))
        {
            other.SetQuietly(true, false);
        }

        Save(s =>
        {
            s.Default.Mode = WallpaperMode.Fixed;
            s.Default.FixedWallpaperId = item.Id;
        });
        IsRotate = false;
    }
}
