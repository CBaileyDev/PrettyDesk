using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.ViewModels;

/// <summary>About page (SPEC §7.5): version, update check, licences, disclaimer and links.</summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    private readonly IAppInfo _info;
    private readonly IUpdateService _updates;
    private readonly ICatalogProvider _catalog;
    private readonly IExternalLauncher _launcher;
    private readonly IUiDispatcher _ui;

    [ObservableProperty]
    private string _updateText = Strings.About_UpdateIdle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck))]
    [NotifyPropertyChangedFor(nameof(CanRestart))]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private UpdateStateKind _updateKind = UpdateStateKind.Idle;

    [ObservableProperty]
    private int _updatePercent;

    [ObservableProperty]
    private bool _showLicenses;

    public AboutViewModel(IAppInfo info, IUpdateService updates, ICatalogProvider catalog, IExternalLauncher launcher, IUiDispatcher ui)
    {
        _info = info;
        _updates = updates;
        _catalog = catalog;
        _launcher = launcher;
        _ui = ui;
        _updates.Changed += OnUpdateChanged;
        ApplyState(_updates.State);
    }

    public string VersionText => Strings.Format(Strings.About_Version, _info.Version);

    public string Licenses => _info.ThirdPartyNotices;

    /// <summary>The catalog's disclaimer when present (it ships with the content), otherwise the built-in one.</summary>
    public string Disclaimer => string.IsNullOrWhiteSpace(_catalog.Current.Disclaimer) ? Strings.About_Disclaimer : _catalog.Current.Disclaimer;

    public bool CanCheck => UpdateKind is not (UpdateStateKind.Checking or UpdateStateKind.Downloading or UpdateStateKind.NotSupported);

    public bool CanRestart => UpdateKind == UpdateStateKind.ReadyToInstall;

    public bool ShowProgress => UpdateKind == UpdateStateKind.Downloading;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        await RunAsync(() => _updates.CheckAsync(), Strings.About_UpdateFailed);
        if (HasError)
        {
            UpdateText = Strings.About_UpdateFailed;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private void RestartToUpdate() => _updates.ApplyAndRestart();

    [RelayCommand]
    private void ToggleLicenses() => ShowLicenses = !ShowLicenses;

    [RelayCommand]
    private void OpenProjectPage() => _launcher.OpenUrl(AppLinks.Repository);

    [RelayCommand]
    private void OpenIssues() => _launcher.OpenUrl(AppLinks.Issues);

    [RelayCommand]
    private void OpenPrivacy() => _launcher.OpenUrl(AppLinks.Privacy);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updates.Changed -= OnUpdateChanged;
        }

        base.Dispose(disposing);
    }

    partial void OnUpdateKindChanged(UpdateStateKind value)
    {
        CheckCommand.NotifyCanExecuteChanged();
        RestartToUpdateCommand.NotifyCanExecuteChanged();
    }

    private void OnUpdateChanged() => _ui.Post(() => ApplyState(_updates.State));

    private void ApplyState(UpdateState state)
    {
        UpdateKind = state.Kind;
        UpdatePercent = state.Percent;
        UpdateText = state.Kind switch
        {
            UpdateStateKind.Checking => Strings.About_UpdateChecking,
            UpdateStateKind.UpToDate => Strings.About_UpdateUpToDate,
            UpdateStateKind.Available => Strings.Format(Strings.About_UpdateAvailable, state.Version),
            UpdateStateKind.Downloading => Strings.Format(Strings.About_UpdateDownloading, state.Version, state.Percent),
            UpdateStateKind.ReadyToInstall => Strings.Format(Strings.About_UpdateReady, state.Version),
            UpdateStateKind.Failed => Strings.About_UpdateFailed,
            UpdateStateKind.NotSupported => Strings.About_UpdateNotSupported,
            _ => Strings.About_UpdateIdle,
        };
    }
}
