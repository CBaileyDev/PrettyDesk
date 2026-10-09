using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.ViewModels;

public enum LibraryFilter
{
    All,
    Installed,
    Enabled,
}

/// <summary>A game tile: our own art (never publisher logos, SPEC §10), status chip and enable toggle.</summary>
public sealed partial class GameCardViewModel : ObservableObject
{
    private bool _quiet;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string? _thumbnailPath;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private PackStateKind _packState;

    [ObservableProperty]
    private bool _isInstalled;

    public GameCardViewModel(GameListing listing) => Listing = listing;

    public GameListing Listing { get; }

    public string Id => Listing.Id;

    public string Name => Listing.DisplayName;

    public bool IsCustom => Listing.IsCustom;

    public string ToggleLabel => Strings.Format(Strings.Library_GameToggle, Name);

    /// <summary>The first letter, shown on the designed placeholder tile until art is available.</summary>
    public string Initial => Name.Length == 0 ? "?" : Name[..1].ToUpperInvariant();

    public Action<GameCardViewModel>? EnabledChanged { get; set; }

    public void SetEnabledQuietly(bool value)
    {
        _quiet = true;
        try
        {
            IsEnabled = value;
        }
        finally
        {
            _quiet = false;
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_quiet)
        {
            EnabledChanged?.Invoke(this);
        }
    }
}

/// <summary>The Library page: searchable, filterable grid of game cards, plus game detail and "Add a game" (SPEC §7.2).</summary>
public sealed partial class LibraryViewModel : ViewModelBase
{
    private readonly ICatalogProvider _catalog;
    private readonly ISettingsProvider _settings;
    private readonly IContentBrowser _content;
    private readonly IContentLibrary _library;
    private readonly IInstalledGamesProvider _installed;
    private readonly IUiDispatcher _ui;
    private readonly Func<GameListing, Action, GameDetailViewModel> _detailFactory;
    private readonly Func<AddGameViewModel> _addFactory;
    private readonly List<GameCardViewModel> _cards = [];
    private IReadOnlySet<string> _installedIds = new HashSet<string>();
    private bool _syncing;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private LibraryFilter _filter = LibraryFilter.All;

    [ObservableProperty]
    private GameDetailViewModel? _detail;

    [ObservableProperty]
    private AddGameViewModel? _addGame;

    [ObservableProperty]
    private string? _toast;

    public LibraryViewModel(
        ICatalogProvider catalog,
        ISettingsProvider settings,
        IContentBrowser content,
        IContentLibrary library,
        IInstalledGamesProvider installed,
        IUiDispatcher ui,
        Func<GameListing, Action, GameDetailViewModel> detailFactory,
        Func<AddGameViewModel> addFactory)
    {
        _catalog = catalog;
        _settings = settings;
        _content = content;
        _library = library;
        _installed = installed;
        _ui = ui;
        _detailFactory = detailFactory;
        _addFactory = addFactory;

        _catalog.Changed += OnModelChanged;
        _settings.Changed += OnModelChanged;
        _content.PackChanged += OnPackChanged;
        _content.ProgressChanged += OnProgress;
        Rebuild();
    }

    public ObservableCollection<GameCardViewModel> Games { get; } = [];

    public bool HasGames => Games.Count > 0;

    public bool IsListVisible => Detail is null && AddGame is null;

    public string EmptyTitle => EmptyState().Title;

    public string EmptyBody => EmptyState().Body;

    /// <summary>Loads which games are installed (Steam/Epic manifests) off the UI thread, then refreshes the chips.</summary>
    [RelayCommand]
    private async Task LoadInstalledAsync()
    {
        await RunAsync(async () =>
        {
            _installedIds = await _installed.GetInstalledGameIdsAsync();
            ApplyChips();
            ApplyFilter();
        }, Strings.Library_EmptyInstalledBody);
    }

    [RelayCommand]
    private void OpenGame(GameCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        Detail?.Dispose();
        Detail = _detailFactory(card.Listing, CloseDetail);
    }

    [RelayCommand]
    private void CloseDetail()
    {
        Detail?.Dispose();
        Detail = null;
        Rebuild();
    }

    [RelayCommand]
    private void OpenAddGame()
    {
        AddGame?.Dispose();
        var vm = _addFactory();
        vm.Finished = added =>
        {
            AddGame?.Dispose();
            AddGame = null;
            Toast = added is null ? null : Strings.Format(Strings.Add_Saved, added);
            Rebuild();
        };
        AddGame = vm;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Search = string.Empty;
        Filter = LibraryFilter.All;
    }

    partial void OnSearchChanged(string value) => ApplyFilter();

    partial void OnFilterChanged(LibraryFilter value) => ApplyFilter();

    partial void OnDetailChanged(GameDetailViewModel? value) => OnPropertyChanged(nameof(IsListVisible));

    partial void OnAddGameChanged(AddGameViewModel? value) => OnPropertyChanged(nameof(IsListVisible));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _catalog.Changed -= OnModelChanged;
            _settings.Changed -= OnModelChanged;
            _content.PackChanged -= OnPackChanged;
            _content.ProgressChanged -= OnProgress;
            Detail?.Dispose();
            AddGame?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnModelChanged() => _ui.Post(() =>
    {
        if (!_syncing)
        {
            Rebuild();
        }
    });

    private void OnPackChanged(string packId) => _ui.Post(ApplyChips);

    private void OnProgress(PackProgress progress) => _ui.Post(ApplyChips);

    private void Rebuild()
    {
        var settings = _settings.Current;
        var catalog = _catalog.Current;
        _cards.Clear();
        foreach (var listing in GameListings.Build(catalog, settings))
        {
            var card = new GameCardViewModel(listing);
            card.SetEnabledQuietly(settings.IsGameEnabled(listing.Id));
            card.EnabledChanged = OnEnabledChanged;
            _cards.Add(card);
        }

        ApplyChips();
        ApplyFilter();
    }

    private void OnEnabledChanged(GameCardViewModel card)
    {
        _syncing = true;
        try
        {
            _settings.Update(s => s.GetGame(card.Id).Enabled = card.IsEnabled);
        }
        finally
        {
            _syncing = false;
        }

        if (Filter == LibraryFilter.Enabled)
        {
            ApplyFilter();
        }
    }

    private void ApplyChips()
    {
        var catalog = _catalog.Current;
        foreach (var card in _cards)
        {
            card.IsInstalled = _installedIds.Contains(card.Id);
            var wallpaperIds = card.IsCustom
                ? _settings.Current.CustomGames.FirstOrDefault(c => c.Id == card.Id)?.Wallpapers ?? []
                : _library.GetWallpaperIds(card.Listing.PackId ?? string.Empty);

            var darkOnly = _settings.Current.General.DarkWallpapersOnly;
            var visibleIds = wallpaperIds.Where(id => ToneFilter.Allows(ToneFilter.ToneOf(catalog, _library, id), darkOnly)).ToList();
            var hero = catalog.FindPack(card.Listing.PackId ?? string.Empty)?.Wallpapers
                .FirstOrDefault(w => w.Role == "hero" && ToneFilter.Allows(w.Tone, darkOnly))?.Id;
            card.ThumbnailPath = (hero is null ? null : _content.GetPreviewImagePath(hero))
                ?? visibleIds.Select(id => _content.GetPreviewImagePath(id)).FirstOrDefault(p => p is not null);

            if (card.IsCustom)
            {
                card.PackState = PackStateKind.Ready;
                card.StatusText = Strings.Library_ChipCustom;
                continue;
            }

            var state = _content.GetPackState(card.Listing.PackId ?? string.Empty);
            card.PackState = state.State;
            card.StatusText = state.State switch
            {
                PackStateKind.Downloading => Strings.Format(Strings.Library_ChipDownloading, (int)(state.Fraction * 100)),
                PackStateKind.Failed => Strings.Library_ChipFailed,
                PackStateKind.Ready => Strings.Library_ChipReady,
                PackStateKind.Unavailable => Strings.Library_ChipUnavailable,
                _ => card.IsInstalled ? Strings.Library_ChipInstalled : Strings.Library_ChipNotDownloaded,
            };
        }
    }

    private void ApplyFilter()
    {
        var term = Search.Trim();
        var visible = _cards
            .Where(c => term.Length == 0 || c.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Where(c => Filter switch
            {
                LibraryFilter.Installed => c.IsInstalled,
                LibraryFilter.Enabled => c.IsEnabled,
                _ => true,
            })
            .OrderByDescending(c => c.IsInstalled)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Games.Clear();
        foreach (var card in visible)
        {
            Games.Add(card);
        }

        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyBody));
    }

    private (string Title, string Body) EmptyState()
    {
        if (_cards.Count == 0)
        {
            return (Strings.Library_EmptyCatalogTitle, Strings.Library_EmptyCatalogBody);
        }

        if (Search.Trim().Length > 0)
        {
            return (Strings.Library_EmptySearchTitle, Strings.Library_EmptySearchBody);
        }

        return Filter switch
        {
            LibraryFilter.Installed => (Strings.Library_EmptyInstalledTitle, Strings.Library_EmptyInstalledBody),
            LibraryFilter.Enabled => (Strings.Library_EmptyEnabledTitle, Strings.Library_EmptyEnabledBody),
            _ => (Strings.Library_EmptySearchTitle, Strings.Library_EmptySearchBody),
        };
    }
}
