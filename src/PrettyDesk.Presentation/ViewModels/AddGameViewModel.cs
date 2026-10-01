using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.ViewModels;

/// <summary>"Add a game" (FR-CUSTOM-1/2/3): pick a running app or browse to an exe, name it and choose its wallpapers.</summary>
public sealed partial class AddGameViewModel : ViewModelBase
{
    private readonly ISettingsProvider _settings;
    private readonly ICatalogProvider _catalog;
    private readonly IRunningAppsProvider _running;
    private readonly IFilePicker _files;
    private readonly IUiDispatcher _ui;
    private readonly Func<IEnumerable<string>, WallpaperPickerViewModel> _pickerFactory;
    private List<RunningApp> _apps = [];

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _exeName;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private PickerState? _picker;

    [ObservableProperty]
    private RunningApp? _selectedApp;

    public AddGameViewModel(
        ISettingsProvider settings,
        ICatalogProvider catalog,
        IRunningAppsProvider running,
        IFilePicker files,
        IUiDispatcher ui,
        Func<IEnumerable<string>, WallpaperPickerViewModel> pickerFactory)
    {
        _settings = settings;
        _catalog = catalog;
        _running = running;
        _files = files;
        _ui = ui;
        _pickerFactory = pickerFactory;
    }

    /// <summary>Called with the new game's name when saved, or null when cancelled.</summary>
    public Action<string?>? Finished { get; set; }

    public ObservableCollection<RunningApp> RunningApps { get; } = [];

    public ObservableCollection<string> Wallpapers { get; } = [];

    public bool HasRunningApps => RunningApps.Count > 0;

    public bool HasWallpapers => Wallpapers.Count > 0;

    public bool CanSave => !IsBusy && !string.IsNullOrWhiteSpace(ExeName) && !string.IsNullOrWhiteSpace(DisplayName) && Wallpapers.Count > 0;

    /// <summary>Loads running apps with a visible window, off the UI thread (it enumerates windows).</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            var apps = await Task.Run(_running.GetWindowedApps);
            _apps = apps.OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            ApplySearch();
        }, Strings.Add_RunningEmpty);
    }

    [RelayCommand]
    private void BrowseExe()
    {
        if (_files.PickExecutable() is { } path)
        {
            Choose(WindowsPaths.FileName(path), WindowsPaths.FileNameWithoutExtension(path));
        }
    }

    [RelayCommand]
    private void OpenPicker() => Picker = new PickerState(_pickerFactory(Wallpapers));

    [RelayCommand]
    private void ConfirmPicker()
    {
        if (Picker is null)
        {
            return;
        }

        var chosen = Picker.Content.SelectedIds;
        Picker = null;
        Wallpapers.Clear();
        foreach (var id in chosen)
        {
            Wallpapers.Add(id);
        }

        OnPropertyChanged(nameof(HasWallpapers));
        OnPropertyChanged(nameof(CanSave));
    }

    [RelayCommand]
    private void CancelPicker() => Picker = null;

    [RelayCommand]
    private void Cancel() => Finished?.Invoke(null);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!Validate())
        {
            return;
        }

        var id = "custom-" + Guid.NewGuid().ToString("N")[..8];
        var name = DisplayName.Trim();
        try
        {
            _settings.Update(s => s.CustomGames.Add(new CustomGame
            {
                Id = id,
                DisplayName = name,
                ExeNames = [ExeName!],
                Wallpapers = Wallpapers.ToList(),
            }));
        }
#pragma warning disable CA1031 // Any persistence failure becomes a friendly message.
        catch (Exception)
#pragma warning restore CA1031
        {
            ValidationMessage = Strings.Add_SaveFailed;
            return;
        }

        Finished?.Invoke(name);
    }

    partial void OnSearchChanged(string value) => ApplySearch();

    partial void OnDisplayNameChanged(string value) => UpdateCanSave();

    partial void OnExeNameChanged(string? value) => UpdateCanSave();

    partial void OnSelectedAppChanged(RunningApp? value)
    {
        if (value is not null)
        {
            Choose(value.ExeName, value.Title);
        }
    }

    private void Choose(string exe, string suggestedName)
    {
        ExeName = exe;
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = suggestedName;
        }

        Validate(full: false);
    }

    /// <summary>Rejects launchers/helpers and exes another game already covers (FR-DET-1, FR-CUSTOM-2).</summary>
    private bool Validate(bool full = true)
    {
        ValidationMessage = null;
        if (string.IsNullOrWhiteSpace(ExeName))
        {
            ValidationMessage = Strings.Add_NoExe;
            return false;
        }

        if (!GameDetailViewModel.IsValidExeName(ExeName))
        {
            ValidationMessage = Strings.Game_ExeInvalid;
            return false;
        }

        if (_catalog.Current.ExcludeExeNames.Contains(ExeName, StringComparer.OrdinalIgnoreCase))
        {
            ValidationMessage = Strings.Format(Strings.Add_LauncherBlocked, ExeName);
            return false;
        }

        var settings = _settings.Current;
        var covered = GameListings.Build(_catalog.Current, settings).FirstOrDefault(g => g.Rules.ExeNames.Contains(ExeName, StringComparer.OrdinalIgnoreCase));
        if (covered is not null)
        {
            ValidationMessage = Strings.Format(Strings.Add_AlreadyCovered, ExeName, covered.DisplayName);
            return false;
        }

        if (!full)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            ValidationMessage = Strings.Add_NeedName;
            return false;
        }

        if (Wallpapers.Count == 0)
        {
            ValidationMessage = Strings.Add_NeedWallpaper;
            return false;
        }

        return true;
    }

    private void ApplySearch()
    {
        var term = Search.Trim();
        RunningApps.Clear();
        foreach (var app in _apps.Where(a => term.Length == 0 || a.Title.Contains(term, StringComparison.OrdinalIgnoreCase) || a.ExeName.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            RunningApps.Add(app);
        }

        OnPropertyChanged(nameof(HasRunningApps));
    }

    private void UpdateCanSave()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
