using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.ViewModels;

/// <summary>Game detail page (SPEC §7.2): enable, mode, wallpaper strip, rotation, detection summary and custom-game editing.</summary>
public sealed partial class GameDetailViewModel : SettingsSectionViewModel
{
    private readonly GameListing _game;
    private readonly ICatalogProvider _catalog;
    private readonly IContentBrowser _content;
    private readonly IContentLibrary _library;
    private readonly IWallpaperController _controller;
    private readonly IMonitorProvider _monitors;
    private readonly IDialogService _dialogs;
    private readonly IFilePicker _files;
    private readonly IExternalLauncher _launcher;
    private readonly IUiDispatcher _ui;
    private readonly Func<IEnumerable<string>, WallpaperPickerViewModel> _pickerFactory;
    private readonly Action _onRemoved;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private bool _prefetchWallpapers = true;

    [ObservableProperty]
    private bool _isRotate = true;

    [ObservableProperty]
    private bool _isShuffle = true;

    [ObservableProperty]
    private IntervalChoice? _selectedInterval;

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private string _packStatusText = string.Empty;

    [ObservableProperty]
    private string? _packStatusHelp;

    [ObservableProperty]
    private bool _canDownload;

    [ObservableProperty]
    private string _exeNamesText = string.Empty;

    [ObservableProperty]
    private string? _exeError;

    [ObservableProperty]
    private PickerState? _picker;

    public GameDetailViewModel(
        GameListing game,
        ISettingsProvider settings,
        ICatalogProvider catalog,
        IContentBrowser content,
        IContentLibrary library,
        IWallpaperController controller,
        IMonitorProvider monitors,
        IDialogService dialogs,
        IFilePicker files,
        IExternalLauncher launcher,
        IUiDispatcher ui,
        Func<IEnumerable<string>, WallpaperPickerViewModel> pickerFactory,
        Action onRemoved)
        : base(settings, ui)
    {
        _game = game;
        _catalog = catalog;
        _content = content;
        _library = library;
        _controller = controller;
        _monitors = monitors;
        _dialogs = dialogs;
        _files = files;
        _launcher = launcher;
        _ui = ui;
        _pickerFactory = pickerFactory;
        _onRemoved = onRemoved;

        IntervalChoices = IntervalLabels.Presets(includeSession: true);
        _content.PackChanged += OnContentChanged;
        _content.ProgressChanged += OnProgress;
        _catalog.Changed += OnCatalogChanged;
        InitialLoad();
    }

    public string Name => _game.DisplayName;

    public bool IsCustom => _game.IsCustom;

    public IReadOnlyList<IntervalChoice> IntervalChoices { get; }

    public ObservableCollection<WallpaperItemViewModel> Wallpapers { get; } = [];

    public bool HasWallpapers => Wallpapers.Count > 0;

    public bool ShowRotationOptions => IsRotate && Wallpapers.Count > 1;

    public IReadOnlyList<string> DetectionLines
    {
        get
        {
            var lines = _game.Rules.ExeNames.Select(e => Strings.Format(Strings.Game_DetectionExe, e)).ToList();
            lines.AddRange(_game.Rules.SteamAppIds.Select(i => Strings.Format(Strings.Game_DetectionSteam, i)));
            return lines.Count == 0 ? [Strings.Game_DetectionNone] : lines;
        }
    }

    protected override void Load(AppSettings settings)
    {
        var game = settings.Games.GetValueOrDefault(_game.Id) ?? new GameSettings();
        Enabled = game.Enabled;
        PrefetchWallpapers = game.PrefetchWallpapers;
        IsRotate = game.Mode == WallpaperMode.Rotate;
        IsShuffle = game.Order == RotationOrder.Shuffle;
        SelectedInterval = IntervalChoices.FirstOrDefault(c => c.Value == game.Interval) ?? new IntervalChoice(game.Interval, IntervalLabels.Of(game.Interval));
        if (IsCustom && settings.CustomGames.FirstOrDefault(c => c.Id == _game.Id) is { } custom)
        {
            ExeNamesText = string.Join(Environment.NewLine, custom.ExeNames);
        }

        RebuildWallpapers(settings, game);
        RefreshPackStatus();
    }

    partial void OnEnabledChanged(bool value) => Save(s => s.GetGame(_game.Id).Enabled = value);

    partial void OnPrefetchWallpapersChanged(bool value) => Save(s => s.GetGame(_game.Id).PrefetchWallpapers = value);

    partial void OnIsRotateChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRotationOptions));
        Save(s =>
        {
            var game = s.GetGame(_game.Id);
            game.Mode = value ? WallpaperMode.Rotate : WallpaperMode.Fixed;
            if (!value && game.FixedWallpaperId is null)
            {
                game.FixedWallpaperId = Wallpapers.FirstOrDefault(w => w.IsIncluded && w.IsAvailable)?.Id ?? Wallpapers.FirstOrDefault()?.Id;
            }
        });
    }

    partial void OnIsShuffleChanged(bool value) => Save(s => s.GetGame(_game.Id).Order = value ? RotationOrder.Shuffle : RotationOrder.Sequential);

    partial void OnSelectedIntervalChanged(IntervalChoice? value)
    {
        if (value is not null)
        {
            Save(s => s.GetGame(_game.Id).Interval = value.Value);
        }
    }

    partial void OnExeNamesTextChanged(string value)
    {
        if (!IsCustom)
        {
            return;
        }

        var names = value.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0 || names.Any(n => !IsValidExeName(n)))
        {
            ExeError = Strings.Game_ExeInvalid;
            return;
        }

        ExeError = null;
        Save(s =>
        {
            var custom = s.CustomGames.FirstOrDefault(c => c.Id == _game.Id);
            if (custom is not null)
            {
                custom.ExeNames = names;
            }
        });
    }

    public static bool IsValidExeName(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && name.Length > 4
        && name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) < 0;

    [RelayCommand]
    private void Back() => _onRemoved();

    [RelayCommand]
    private void Preview(WallpaperItemViewModel? item)
    {
        if (item is not null && item.IsAvailable)
        {
            _controller.Preview(item.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private void Download()
    {
        if (_game.PackId is { } packId)
        {
            if (_content.GetPackState(packId).State == PackStateKind.Failed)
            {
                _content.RetryPack(packId, _monitors.GetMonitors());
            }
            else
            {
                _content.RequestPack(packId, _monitors.GetMonitors());
            }
            RefreshPackStatus();
        }
    }

    [RelayCommand]
    private void Suggest() => _launcher.OpenUrl(AppLinks.SuggestGame(_game.DisplayName, (_game.Rules.ExeNames.Count > 0 ? _game.Rules.ExeNames[0] : string.Empty)));

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (!IsCustom || !await _dialogs.ConfirmAsync(Strings.Format(Strings.Game_RemoveTitle, _game.DisplayName), Strings.Game_RemoveMessage, Strings.Game_RemoveGame, Strings.Common_Cancel))
        {
            return;
        }

        Settings.Update(s =>
        {
            s.CustomGames.RemoveAll(c => c.Id == _game.Id);
            s.Games.Remove(_game.Id);
        });
        _onRemoved();
    }

    [RelayCommand]
    private void OpenPicker()
    {
        var existing = Wallpapers.Select(w => w.Id).ToList();
        Picker = new PickerState(_pickerFactory(existing));
    }

    [RelayCommand]
    private void ConfirmPicker()
    {
        if (Picker is null)
        {
            return;
        }

        var chosen = Picker.Content.SelectedIds;
        Picker = null;
        if (chosen.Count > 0)
        {
            Save(s =>
            {
                var custom = s.CustomGames.FirstOrDefault(c => c.Id == _game.Id);
                if (custom is not null)
                {
                    custom.Wallpapers = chosen.ToList();
                }
            });
        }
    }

    [RelayCommand]
    private void CancelPicker() => Picker = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content.PackChanged -= OnContentChanged;
            _content.ProgressChanged -= OnProgress;
            _catalog.Changed -= OnCatalogChanged;
        }

        base.Dispose(disposing);
    }

    private void OnContentChanged(string packId) => _ui.Post(() =>
    {
        if (packId == _game.PackId || packId == "user")
        {
            Load(Settings.Current);
        }
    });

    private void OnCatalogChanged() => _ui.Post(() => Load(Settings.Current));

    private void OnProgress(PackProgress progress) => _ui.Post(() =>
    {
        if (progress.PackId == _game.PackId)
        {
            RefreshPackStatus();
        }
    });

    private void RebuildWallpapers(AppSettings settings, GameSettings game)
    {
        var catalog = _catalog.Current;
        IEnumerable<string> ids = IsCustom
            ? settings.CustomGames.FirstOrDefault(c => c.Id == _game.Id)?.Wallpapers ?? []
            : _library.GetWallpaperIds(_game.PackId ?? string.Empty);

        var excluded = game.Excluded.ToHashSet(StringComparer.Ordinal);
        Wallpapers.Clear();
        foreach (var id in ids)
        {
            var item = new WallpaperItemViewModel(id, WallpaperTitles.Resolve(catalog, id))
            {
                PreviewPath = _content.GetPreviewImagePath(id),
                IsAvailable = _library.TryGetAsset(id) is not null,
            };
            item.SetQuietly(!excluded.Contains(id), game.FixedWallpaperId == id && game.Mode == WallpaperMode.Fixed);
            item.IncludedChanged = OnItemIncludedChanged;
            item.FavoriteChanged = OnItemFavoriteChanged;
            Wallpapers.Add(item);
        }

        OnPropertyChanged(nameof(HasWallpapers));
        OnPropertyChanged(nameof(ShowRotationOptions));
    }

    private void OnItemIncludedChanged(WallpaperItemViewModel item)
    {
        if (!item.IsIncluded && Wallpapers.All(w => !w.IsIncluded))
        {
            // At least one wallpaper must stay on, otherwise the game would have nothing to show.
            item.SetQuietly(true, item.IsFavorite);
            Notice = Strings.Game_KeepOne;
            return;
        }

        Notice = null;
        var excluded = Wallpapers.Where(w => !w.IsIncluded).Select(w => w.Id).ToList();
        Save(s => s.GetGame(_game.Id).Excluded = excluded);
    }

    private void OnItemFavoriteChanged(WallpaperItemViewModel item)
    {
        foreach (var other in Wallpapers.Where(w => w != item && w.IsFavorite))
        {
            other.SetQuietly(other.IsIncluded, false);
        }

        Save(s =>
        {
            var game = s.GetGame(_game.Id);
            if (item.IsFavorite)
            {
                game.Mode = WallpaperMode.Fixed;
                game.FixedWallpaperId = item.Id;
            }
            else
            {
                game.Mode = WallpaperMode.Rotate;
                game.FixedWallpaperId = null;
            }
        });
        IsRotate = !item.IsFavorite;
    }

    private void RefreshPackStatus()
    {
        PackStatusHelp = null;
        if (_game.PackId is not { } packId)
        {
            PackStatusText = string.Empty;
            CanDownload = false;
            return;
        }

        var state = _content.GetPackState(packId);
        PackStatusText = state.State switch
        {
            PackStateKind.Downloading => Strings.Format(Strings.Game_Downloading, (int)(state.Fraction * 100)),
            PackStateKind.Failed => Strings.Game_DownloadFailed,
            PackStateKind.Ready => Strings.Library_ChipReady,
            PackStateKind.Unavailable => Strings.Library_ChipUnavailable,
            _ => Strings.Library_ChipNotDownloaded,
        };
        CanDownload = state.State is PackStateKind.NotDownloaded or PackStateKind.Failed;
        if (state.State == PackStateKind.Unavailable)
        {
            PackStatusHelp = Strings.Game_PackUnavailable;
        }
        DownloadCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Wraps the picker so the page can show it as an overlay and bind to its commands.</summary>
public sealed class PickerState
{
    public PickerState(WallpaperPickerViewModel content) => Content = content;

    public WallpaperPickerViewModel Content { get; }
}
