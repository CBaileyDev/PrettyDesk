using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Presentation.Formatting;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.ViewModels;

public enum PageKind
{
    Home,
    Library,
    Defaults,
    Settings,
    About,
}

public sealed record NavItem(PageKind Kind, string Label, string Glyph);

/// <summary>
/// The main window's navigation (SPEC §7). Pages are created on demand and released with the window, which keeps idle memory
/// low when the window is closed to the tray (FR-APP-4).
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly Dictionary<PageKind, Func<ViewModelBase>> _factories;
    private readonly Dictionary<PageKind, ViewModelBase> _pages = [];

    [ObservableProperty]
    private PageKind _selectedKind = PageKind.Home;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    public ShellViewModel(
        Func<HomeViewModel> home,
        Func<LibraryViewModel> library,
        Func<DefaultsViewModel> defaults,
        Func<SettingsViewModel> settings,
        Func<AboutViewModel> about)
    {
        _factories = new Dictionary<PageKind, Func<ViewModelBase>>
        {
            [PageKind.Home] = home,
            [PageKind.Library] = library,
            [PageKind.Defaults] = defaults,
            [PageKind.Settings] = settings,
            [PageKind.About] = about,
        };

        // Segoe Fluent Icons code points.
        NavItems =
        [
            new(PageKind.Home, Strings.Nav_Home, ""),
            new(PageKind.Library, Strings.Nav_Library, ""),
            new(PageKind.Defaults, Strings.Nav_Defaults, ""),
            new(PageKind.Settings, Strings.Nav_Settings, ""),
            new(PageKind.About, Strings.Nav_About, ""),
        ];
        NavigateTo(PageKind.Home);
    }

    public IReadOnlyList<NavItem> NavItems { get; }

    [RelayCommand]
    private void NavigateTo(PageKind kind)
    {
        SelectedKind = kind;
        if (!_pages.TryGetValue(kind, out var page))
        {
            page = _factories[kind]();
            _pages[kind] = page;
        }

        CurrentPage = page;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var page in _pages.Values)
            {
                page.Dispose();
            }

            _pages.Clear();
            CurrentPage = null;
        }

        base.Dispose(disposing);
    }
}

/// <summary>The tray menu's content and commands (FR-APP-2): a status line, Next, Pause ▸ (1 h / until resumed) or Resume, Open, Quit.</summary>
public sealed partial class TrayViewModel : ViewModelBase
{
    private readonly IWallpaperController _controller;
    private readonly IAppController _app;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;

    [ObservableProperty]
    private string _statusLine = Strings.Status_Starting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(CanSkip))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isBlocked;

    [ObservableProperty]
    private OrchestratorMode _mode;

    public TrayViewModel(IWallpaperController controller, IAppController app, IUiDispatcher ui, TimeProvider time)
    {
        _controller = controller;
        _app = app;
        _ui = ui;
        _time = time;
        _controller.StatusChanged += OnStatusChanged;
        Apply(_controller.Status);
    }

    public bool IsRunning => !IsPaused;

    public bool CanPause => !IsPaused && !IsBlocked;

    public bool CanSkip => Mode is OrchestratorMode.Default or OrchestratorMode.Game or OrchestratorMode.GameGrace;

    [RelayCommand(CanExecute = nameof(CanSkip))]
    private void Next() => _controller.NextWallpaper();

    [RelayCommand]
    private void PauseOneHour() => _controller.Pause(TimeSpan.FromHours(1));

    [RelayCommand]
    private void PauseUntilResumed() => _controller.Pause(null);

    [RelayCommand]
    private void Resume() => _controller.Resume();

    [RelayCommand]
    private void Open() => _app.ShowMainWindow();

    [RelayCommand]
    private void Quit() => _app.Quit();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.StatusChanged -= OnStatusChanged;
        }

        base.Dispose(disposing);
    }

    private void OnStatusChanged(OrchestratorStatus status) => _ui.Post(() => Apply(status));

    private void Apply(OrchestratorStatus status)
    {
        StatusLine = StatusFormatter.Describe(status, _time.GetUtcNow()).Headline;
        IsPaused = status.Mode == OrchestratorMode.Paused;
        IsBlocked = status.Mode == OrchestratorMode.Blocked;
        Mode = status.Mode;
        NextCommand.NotifyCanExecuteChanged();
    }
}
