using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.ViewModels;

public enum OnboardingStep
{
    Welcome,
    Style,
    Mode,
    Games,
    Startup,
    Done,
}

public sealed partial class StyleOption : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public StyleOption(SetupStyle style, string title, string help)
    {
        Style = style;
        Title = title;
        Help = help;
    }

    public SetupStyle Style { get; }

    public string Title { get; }

    public string Help { get; }
}

public sealed partial class OnboardingGameViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled = true;

    public OnboardingGameViewModel(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }
}

/// <summary>First-run flow (SPEC §2.1): welcome → setup style → default mode → installed games → start with Windows → done.</summary>
public sealed partial class OnboardingViewModel : ViewModelBase
{
    public const int QuestionSteps = 5;

    private readonly ISettingsProvider _settings;
    private readonly ICatalogProvider _catalog;
    private readonly IInstalledGamesProvider _installed;
    private readonly IStartupService _startup;
    private readonly IEnvironmentConflictSource _conflicts;
    private readonly IContentBrowser _content;
    private readonly IMonitorProvider _monitors;
    private bool _gamesLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepNumber))]
    [NotifyPropertyChangedFor(nameof(StepText))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    [NotifyPropertyChangedFor(nameof(NextText))]
    private OnboardingStep _step = OnboardingStep.Welcome;

    [ObservableProperty]
    private SetupStyle _selectedStyle = SetupStyle.SurpriseMe;

    [ObservableProperty]
    private bool _isRotate = true;

    [ObservableProperty]
    private IntervalChoice? _selectedInterval;

    [ObservableProperty]
    private bool _startWithWindows = true;

    [ObservableProperty]
    private bool _isLoadingGames;

    public OnboardingViewModel(
        ISettingsProvider settings,
        ICatalogProvider catalog,
        IInstalledGamesProvider installed,
        IStartupService startup,
        IEnvironmentConflictSource conflicts,
        IContentBrowser content,
        IMonitorProvider monitors)
    {
        _settings = settings;
        _catalog = catalog;
        _installed = installed;
        _startup = startup;
        _conflicts = conflicts;
        _content = content;
        _monitors = monitors;

        IntervalChoices = IntervalLabels.Presets(includeSession: false);
        SelectedInterval = IntervalChoices.First(c => c.Value == RotationInterval.Every(TimeSpan.FromMinutes(30)));
        StyleOptions =
        [
            new(SetupStyle.MatteBlack, Strings.Onboard_StyleMatteBlack, Strings.Onboard_StyleMatteBlackHelp),
            new(SetupStyle.CleanWhite, Strings.Onboard_StyleCleanWhite, Strings.Onboard_StyleCleanWhiteHelp),
            new(SetupStyle.WarmWoodAndPlants, Strings.Onboard_StyleWarmWood, Strings.Onboard_StyleWarmWoodHelp),
            new(SetupStyle.Pastel, Strings.Onboard_StylePastel, Strings.Onboard_StylePastelHelp),
            new(SetupStyle.Rgb, Strings.Onboard_StyleRgb, Strings.Onboard_StyleRgbHelp),
            new(SetupStyle.SurpriseMe, Strings.Onboard_StyleSurprise, Strings.Onboard_StyleSurpriseHelp),
        ];
        ShowSpotlightNote = conflicts.Detect().HasFlag(EnvironmentConflict.SpotlightOrSlideshow);
        MarkSelectedStyle();
    }

    /// <summary>Raised when the user finishes; the window closes and the app minimises to the tray with a one-time tip.</summary>
    public event Action? Completed;

    public IReadOnlyList<StyleOption> StyleOptions { get; }

    public IReadOnlyList<IntervalChoice> IntervalChoices { get; }

    public ObservableCollection<OnboardingGameViewModel> Games { get; } = [];

    public bool HasGames => Games.Count > 0;

    public bool ShowGamesEmpty => _gamesLoaded && !IsLoadingGames && Games.Count == 0;

    public bool ShowSpotlightNote { get; }

    public int StepNumber => Math.Min((int)Step + 1, QuestionSteps);

    public string StepText => Step == OnboardingStep.Done ? string.Empty : Strings.Format(Strings.Onboard_StepOf, StepNumber, QuestionSteps);

    public bool CanGoBack => Step is not (OnboardingStep.Welcome or OnboardingStep.Done) && !IsBusy;

    public bool IsDone => Step == OnboardingStep.Done;

    public string NextText => Step switch
    {
        OnboardingStep.Welcome => Strings.Onboard_Start,
        OnboardingStep.Startup => Strings.Common_Finish,
        OnboardingStep.Done => Strings.Common_Done,
        _ => Strings.Common_Next,
    };

    [RelayCommand]
    private async Task NextAsync()
    {
        switch (Step)
        {
            case OnboardingStep.Welcome:
            case OnboardingStep.Style:
                Step++;
                break;
            case OnboardingStep.Mode:
                Step = OnboardingStep.Games;
                await LoadGamesAsync();
                break;
            case OnboardingStep.Games:
                Step = OnboardingStep.Startup;
                break;
            case OnboardingStep.Startup:
                await FinishAsync();
                break;
            case OnboardingStep.Done:
                Completed?.Invoke();
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (Step > OnboardingStep.Welcome && Step != OnboardingStep.Done)
        {
            Step--;
        }
    }

    [RelayCommand]
    private void ChooseStyle(StyleOption? option)
    {
        if (option is not null)
        {
            SelectedStyle = option.Style;
        }
    }

    partial void OnStepChanged(OnboardingStep value)
    {
        BackCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedStyleChanged(SetupStyle value) => MarkSelectedStyle();

    private void MarkSelectedStyle()
    {
        foreach (var option in StyleOptions)
        {
            option.IsSelected = option.Style == SelectedStyle;
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsBusy))
        {
            OnPropertyChanged(nameof(CanGoBack));
            BackCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task LoadGamesAsync()
    {
        if (_gamesLoaded)
        {
            return;
        }

        IsLoadingGames = true;
        try
        {
            var ids = await _installed.GetInstalledGameIdsAsync();
            var catalog = _catalog.Current;
            Games.Clear();
            foreach (var game in catalog.Games.Where(g => ids.Contains(g.Id)).OrderBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                Games.Add(new OnboardingGameViewModel(game.Id, game.DisplayName));
            }
        }
#pragma warning disable CA1031 // A failed scan just means an empty list; onboarding must never block on it.
        catch (Exception)
#pragma warning restore CA1031
        {
            Games.Clear();
        }
        finally
        {
            _gamesLoaded = true;
            IsLoadingGames = false;
            OnPropertyChanged(nameof(HasGames));
            OnPropertyChanged(nameof(ShowGamesEmpty));
        }
    }

    private async Task FinishAsync()
    {
        await RunAsync(async () =>
        {
            var catalog = _catalog.Current;
            var interval = SelectedInterval?.Value ?? RotationInterval.Every(TimeSpan.FromMinutes(30));
            _settings.Update(s =>
            {
                SetupStyles.Apply(SelectedStyle, catalog, s.Default.Selection);
                s.Default.Mode = IsRotate ? WallpaperMode.Rotate : WallpaperMode.Fixed;
                s.Default.Interval = interval;
                s.Default.FixedWallpaperId = IsRotate ? null : PickFixedWallpaper(catalog, s.Default.Selection);
                foreach (var game in Games)
                {
                    s.GetGame(game.Id).Enabled = game.IsEnabled;
                }

                s.General.StartWithWindows = StartWithWindows;
                s.General.OnboardingCompleted = true;
            });

            _startup.Apply(StartWithWindows);
            Prefetch(catalog);
            Step = OnboardingStep.Done;
            await Task.CompletedTask;
        }, Strings.Onboard_Failed);
    }

    /// <summary>The strongest wallpaper of the first selected collection (the one tagged starter), for "one wallpaper" mode.</summary>
    private static string? PickFixedWallpaper(PrettyDesk.Core.Catalog.CatalogDocument catalog, SelectionSettings selection)
    {
        foreach (var collectionId in selection.Collections)
        {
            var packId = catalog.Collections.FirstOrDefault(c => c.Id == collectionId)?.PackId;
            var pack = packId is null ? null : catalog.FindPack(packId);
            var pick = pack?.Wallpapers.FirstOrDefault(w => w.Starter && !selection.Excluded.Contains(w.Id)) ?? pack?.Wallpapers.FirstOrDefault(w => !selection.Excluded.Contains(w.Id));
            if (pick is not null)
            {
                return pick.Id;
            }
        }

        return selection.Wallpapers.Count > 0 ? selection.Wallpapers[0] : null;
    }

    /// <summary>Start downloading the packs for games the user turned on so wallpapers are ready when they launch (FR-CON-4).</summary>
    private void Prefetch(PrettyDesk.Core.Catalog.CatalogDocument catalog)
    {
        if (!_settings.Current.Content.PrefetchInstalledGames)
        {
            return;
        }

        var monitors = _monitors.GetMonitors();
        foreach (var game in Games.Where(g => g.IsEnabled))
        {
            if (catalog.FindGame(game.Id)?.PackId is { } packId)
            {
                _content.RequestPack(packId, monitors);
            }
        }
    }
}
