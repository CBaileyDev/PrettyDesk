using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Formatting;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.ViewModels;

/// <summary>
/// A settings section: reads from the current snapshot, writes through <see cref="ISettingsProvider.Update"/>, and reloads when
/// the snapshot changes elsewhere. Loading never writes back (no feedback loops).
/// </summary>
public abstract class SettingsSectionViewModel : ViewModelBase
{
    private readonly IUiDispatcher _ui;
    private bool _loading;

    protected SettingsSectionViewModel(ISettingsProvider settings, IUiDispatcher ui)
    {
        Settings = settings;
        _ui = ui;
        settings.Changed += OnSettingsChanged;
    }

    protected ISettingsProvider Settings { get; }

    /// <summary>Call once at the end of the derived constructor.</summary>
    protected void InitialLoad() => Reload();

    protected abstract void Load(AppSettings settings);

    protected void Save(Action<AppSettings> apply)
    {
        if (!_loading)
        {
            Settings.Update(apply);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Settings.Changed -= OnSettingsChanged;
        }

        base.Dispose(disposing);
    }

    private void OnSettingsChanged() => _ui.Post(Reload);

    private void Reload()
    {
        _loading = true;
        try
        {
            Load(Settings.Current);
        }
        finally
        {
            _loading = false;
        }
    }
}

public sealed partial class GeneralSettingsViewModel : SettingsSectionViewModel
{
    private readonly IStartupService _startup;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _restoreOnExit;

    public GeneralSettingsViewModel(ISettingsProvider settings, IStartupService startup, IUiDispatcher ui)
        : base(settings, ui)
    {
        _startup = startup;
        InitialLoad();
    }

    protected override void Load(AppSettings settings)
    {
        StartWithWindows = settings.General.StartWithWindows;
        RestoreOnExit = settings.General.RestoreOnExit;
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        Save(s => s.General.StartWithWindows = value);
        if (_startup.IsEnabled != value)
        {
            try
            {
                _startup.Apply(value);
            }
#pragma warning disable CA1031 // A registry failure must not crash the settings page; it is surfaced as a message.
            catch (Exception)
#pragma warning restore CA1031
            {
                ErrorMessage = Strings.Settings_StartWithWindowsHelp;
            }
        }
    }

    partial void OnRestoreOnExitChanged(bool value) => Save(s => s.General.RestoreOnExit = value);
}

public sealed partial class DetectionSettingsViewModel : SettingsSectionViewModel
{
    public const int MinPollSeconds = 1;
    public const int MaxPollSeconds = 10;
    public const int MaxDetectDelaySeconds = 30;
    public const int MaxExitGraceSeconds = 120;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private int _pollSeconds = 2;

    [ObservableProperty]
    private int _detectDelaySeconds = 3;

    [ObservableProperty]
    private int _exitGraceSeconds = 10;

    [ObservableProperty]
    private bool _unknownGameHints = true;

    public DetectionSettingsViewModel(ISettingsProvider settings, IUiDispatcher ui)
        : base(settings, ui) => InitialLoad();

    public string PollSecondsText => Strings.Format(Strings.Settings_PollSecondsValue, PollSeconds);

    public string DetectDelayText => Strings.Format(Strings.Settings_PollSecondsValue, DetectDelaySeconds);

    public string ExitGraceText => Strings.Format(Strings.Settings_PollSecondsValue, ExitGraceSeconds);

    protected override void Load(AppSettings settings)
    {
        Enabled = settings.Detection.Enabled;
        PollSeconds = settings.Detection.PollSeconds;
        DetectDelaySeconds = settings.Detection.DetectDelaySeconds;
        ExitGraceSeconds = settings.Detection.ExitGraceSeconds;
        UnknownGameHints = settings.Detection.UnknownGameHints;
    }

    partial void OnEnabledChanged(bool value) => Save(s => s.Detection.Enabled = value);

    partial void OnPollSecondsChanged(int value)
    {
        var clamped = Math.Clamp(value, MinPollSeconds, MaxPollSeconds);
        if (clamped != value)
        {
            PollSeconds = clamped;
            return;
        }

        OnPropertyChanged(nameof(PollSecondsText));
        Save(s => s.Detection.PollSeconds = clamped);
    }

    partial void OnDetectDelaySecondsChanged(int value)
    {
        var clamped = Math.Clamp(value, 1, MaxDetectDelaySeconds);
        if (clamped != value)
        {
            DetectDelaySeconds = clamped;
            return;
        }

        OnPropertyChanged(nameof(DetectDelayText));
        Save(s => s.Detection.DetectDelaySeconds = clamped);
    }

    partial void OnExitGraceSecondsChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, MaxExitGraceSeconds);
        if (clamped != value)
        {
            ExitGraceSeconds = clamped;
            return;
        }

        OnPropertyChanged(nameof(ExitGraceText));
        Save(s => s.Detection.ExitGraceSeconds = clamped);
    }

    partial void OnUnknownGameHintsChanged(bool value) => Save(s => s.Detection.UnknownGameHints = value);
}

public sealed partial class MonitorSettingsViewModel : SettingsSectionViewModel
{
    [ObservableProperty]
    private bool _differentPerMonitor;

    [ObservableProperty]
    private bool _gameOnSecondaryOnly;

    public MonitorSettingsViewModel(ISettingsProvider settings, IUiDispatcher ui)
        : base(settings, ui) => InitialLoad();

    protected override void Load(AppSettings settings)
    {
        DifferentPerMonitor = settings.Monitors.Mode == MonitorMode.Different;
        GameOnSecondaryOnly = settings.Monitors.GameOnSecondaryOnly;
    }

    partial void OnDifferentPerMonitorChanged(bool value) => Save(s => s.Monitors.Mode = value ? MonitorMode.Different : MonitorMode.Same);

    partial void OnGameOnSecondaryOnlyChanged(bool value) => Save(s => s.Monitors.GameOnSecondaryOnly = value);
}

public sealed partial class NotificationSettingsViewModel : SettingsSectionViewModel
{
    [ObservableProperty]
    private bool _downloadErrors = true;

    [ObservableProperty]
    private bool _updates = true;

    [ObservableProperty]
    private bool _unknownGames = true;

    public NotificationSettingsViewModel(ISettingsProvider settings, IUiDispatcher ui)
        : base(settings, ui) => InitialLoad();

    protected override void Load(AppSettings settings)
    {
        DownloadErrors = settings.Notifications.DownloadErrors;
        Updates = settings.Notifications.Updates;
        UnknownGames = settings.Notifications.UnknownGames;
    }

    partial void OnDownloadErrorsChanged(bool value) => Save(s => s.Notifications.DownloadErrors = value);

    partial void OnUpdatesChanged(bool value) => Save(s => s.Notifications.Updates = value);

    partial void OnUnknownGamesChanged(bool value) => Save(s => s.Notifications.UnknownGames = value);
}

public sealed partial class StorageSettingsViewModel : SettingsSectionViewModel
{
    public static IReadOnlyList<double> CapChoicesGb { get; } = [1, 2, 3, 5, 10, 20];

    private readonly IContentBrowser _content;
    private readonly IDialogService _dialogs;
    private readonly IDetectionProtectedPacks _protectedPacks;

    [ObservableProperty]
    private double _maxCacheGb = 3;

    [ObservableProperty]
    private bool _prefetchInstalledGames = true;

    [ObservableProperty]
    private string _usageText = string.Empty;

    [ObservableProperty]
    private string? _resultMessage;

    public StorageSettingsViewModel(ISettingsProvider settings, IContentBrowser content, IDialogService dialogs, IDetectionProtectedPacks protectedPacks, IUiDispatcher ui)
        : base(settings, ui)
    {
        _content = content;
        _dialogs = dialogs;
        _protectedPacks = protectedPacks;
        InitialLoad();
        RefreshUsage();
    }

    public string CapText => Strings.Format(Strings.Settings_StorageCapValue, MaxCacheGb.ToString("0.#", Strings.Culture ?? System.Globalization.CultureInfo.CurrentUICulture));

    public void RefreshUsage() => UsageText = Strings.Format(Strings.Settings_StorageUsed, ByteSize.Format(_content.StorageUsageBytes()));

    protected override void Load(AppSettings settings)
    {
        MaxCacheGb = settings.Content.MaxCacheGB;
        PrefetchInstalledGames = settings.Content.PrefetchInstalledGames;
    }

    partial void OnMaxCacheGbChanged(double value)
    {
        OnPropertyChanged(nameof(CapText));
        Save(s => s.Content.MaxCacheGB = value);
    }

    partial void OnPrefetchInstalledGamesChanged(bool value) => Save(s => s.Content.PrefetchInstalledGames = value);

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (!await _dialogs.ConfirmAsync(Strings.Settings_ClearDownloadsTitle, Strings.Settings_ClearDownloadsMessage, Strings.Settings_ClearDownloads, Strings.Common_Cancel))
        {
            return;
        }

        ResultMessage = null;
        await RunAsync(async () =>
        {
            var freed = await Task.Run(() => _content.ClearDownloaded(_protectedPacks.PacksInUse()));
            ResultMessage = Strings.Format(Strings.Settings_ClearDownloadsDone, ByteSize.Format(freed));
            RefreshUsage();
        }, Strings.Settings_ClearDownloadsFailed);
    }
}

/// <summary>Packs that must survive "Clear downloaded content" because they are in active use (FR-CON-6).</summary>
public interface IDetectionProtectedPacks
{
    IReadOnlySet<string> PacksInUse();
}

public sealed record DetectionLogEntry(DateTimeOffset Time, string Text);

public sealed partial class AdvancedSettingsViewModel : ViewModelBase
{
    public const int MaxLogEntries = 100;

    private readonly IDetectionFeed _feed;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly IExternalLauncher _launcher;
    private readonly IAppInfo _info;
    private readonly IDiagnosticsExporter _exporter;
    private readonly IDialogService _dialogs;
    private readonly IAppMaintenance _maintenance;
    private string? _lastKey;

    [ObservableProperty]
    private bool _logPaused;

    [ObservableProperty]
    private string? _exportedPath;

    [ObservableProperty]
    private string? _resultMessage;

    public AdvancedSettingsViewModel(
        IDetectionFeed feed,
        IUiDispatcher ui,
        TimeProvider time,
        IExternalLauncher launcher,
        IAppInfo info,
        IDiagnosticsExporter exporter,
        IDialogService dialogs,
        IAppMaintenance maintenance)
    {
        _feed = feed;
        _ui = ui;
        _time = time;
        _launcher = launcher;
        _info = info;
        _exporter = exporter;
        _dialogs = dialogs;
        _maintenance = maintenance;
        _feed.Observed += OnObserved;
    }

    public ObservableCollection<DetectionLogEntry> Log { get; } = [];

    public bool HasLog => Log.Count > 0;

    private void OnObserved(DetectionObservation observation) => _ui.Post(() =>
    {
        if (LogPaused)
        {
            return;
        }

        // Only changes are interesting; a poll that sees the same thing twice must not scroll the log.
        var key = (observation.ForegroundExe ?? string.Empty) + "|" + string.Join(',', observation.MatchedGameIds);
        if (key == _lastKey)
        {
            return;
        }

        _lastKey = key;
        var app = observation.ForegroundExe ?? Strings.Settings_DetectionLogNoForeground;
        var match = observation.MatchedGameIds.Count == 0
            ? Strings.Settings_DetectionLogNoMatch
            : Strings.Format(Strings.Settings_DetectionLogMatched, string.Join(", ", observation.MatchedGameIds));
        var now = _time.GetLocalNow();
        Log.Insert(0, new DetectionLogEntry(now, Strings.Format(Strings.Settings_DetectionLogLine, now.ToString("T", Strings.Culture ?? System.Globalization.CultureInfo.CurrentUICulture), app, match)));
        while (Log.Count > MaxLogEntries)
        {
            Log.RemoveAt(Log.Count - 1);
        }

        OnPropertyChanged(nameof(HasLog));
    });

    [RelayCommand]
    private void ClearLog()
    {
        Log.Clear();
        _lastKey = null;
        OnPropertyChanged(nameof(HasLog));
    }

    [RelayCommand]
    private void OpenLogs() => _launcher.OpenFolder(_info.LogsDirectory);

    [RelayCommand]
    private void ShowExport()
    {
        if (ExportedPath is not null)
        {
            _launcher.OpenFolder(Path.GetDirectoryName(ExportedPath) ?? ExportedPath);
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        ResultMessage = null;
        ExportedPath = null;
        await RunAsync(async () =>
        {
            ExportedPath = await _exporter.ExportAsync();
            ResultMessage = Strings.Format(Strings.Settings_ExportDone, ExportedPath);
        }, Strings.Settings_ExportFailed);
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        var removeImages = await AskResetAsync();
        if (removeImages is null)
        {
            return;
        }

        ResultMessage = null;
        await RunAsync(async () =>
        {
            await _maintenance.ResetAsync(removeImages.Value);
        }, Strings.Settings_ResetFailed);
    }

    /// <summary>Asks whether to also remove the user's own images. Returns null when the user cancels.</summary>
    private async Task<bool?> AskResetAsync()
    {
        var choice = await _dialogs.ChooseAsync(
            Strings.Settings_ResetTitle,
            Strings.Settings_ResetMessage,
            [Strings.Settings_ResetKeepImages, Strings.Settings_ResetRemoveImages, Strings.Common_Cancel]);

        return choice switch { 0 => false, 1 => true, _ => null };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _feed.Observed -= OnObserved;
        }

        base.Dispose(disposing);
    }
}

public sealed partial class RestoreViewModel : ViewModelBase
{
    private readonly IRestoreService _restore;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private string? _resultMessage;

    public RestoreViewModel(IRestoreService restore, IDialogService dialogs)
    {
        _restore = restore;
        _dialogs = dialogs;
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (!await _dialogs.ConfirmAsync(Strings.Settings_RestoreTitle, Strings.Settings_RestoreMessage, Strings.Settings_RestoreOriginal, Strings.Common_Cancel))
        {
            return;
        }

        ResultMessage = null;
        await RunAsync(async () =>
        {
            var outcome = await _restore.RestoreOriginalAsync();
            ResultMessage = outcome.NothingToRestore ? Strings.Settings_RestoreNothing : outcome.Note ?? Strings.Settings_RestoreDone;
        }, Strings.Settings_RestoreFailed);
    }
}

/// <summary>The Settings page: sections as child view models (SPEC §7.4).</summary>
public sealed class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel(
        GeneralSettingsViewModel general,
        DetectionSettingsViewModel detection,
        MonitorSettingsViewModel monitors,
        StorageSettingsViewModel storage,
        NotificationSettingsViewModel notifications,
        AdvancedSettingsViewModel advanced,
        RestoreViewModel restore)
    {
        General = general;
        Detection = detection;
        Monitors = monitors;
        Storage = storage;
        Notifications = notifications;
        Advanced = advanced;
        Restore = restore;
    }

    public GeneralSettingsViewModel General { get; }

    public DetectionSettingsViewModel Detection { get; }

    public MonitorSettingsViewModel Monitors { get; }

    public StorageSettingsViewModel Storage { get; }

    public NotificationSettingsViewModel Notifications { get; }

    public AdvancedSettingsViewModel Advanced { get; }

    public RestoreViewModel Restore { get; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            General.Dispose();
            Detection.Dispose();
            Monitors.Dispose();
            Storage.Dispose();
            Notifications.Dispose();
            Advanced.Dispose();
            Restore.Dispose();
        }

        base.Dispose(disposing);
    }
}
