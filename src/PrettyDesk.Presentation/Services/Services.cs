namespace PrettyDesk.Presentation.Services;

/// <summary>Marshals work to the UI thread. Tests use an inline implementation.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText);

    /// <summary>Shows a message with several buttons and returns the index of the one pressed, or -1 when dismissed.</summary>
    Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> buttons);

    Task ShowMessageAsync(string title, string message);
}

public interface IFilePicker
{
    /// <summary>Lets the user choose one or more images (JPEG/PNG/WebP/BMP). Empty when cancelled.</summary>
    IReadOnlyList<string> PickImages();

    /// <summary>Lets the user choose a game executable, or null when cancelled.</summary>
    string? PickExecutable();
}

/// <summary>An app with a visible window, offered in "Add a game" (FR-CUSTOM-1).</summary>
public sealed record RunningApp(string ExeName, string Title);

public interface IRunningAppsProvider
{
    IReadOnlyList<RunningApp> GetWindowedApps();
}

public interface IExternalLauncher
{
    void OpenFolder(string path);

    void OpenUrl(string url);
}

public interface IStartupService
{
    bool IsEnabled { get; }

    void Apply(bool enabled);
}

public interface IAppController
{
    void ShowMainWindow();

    void Quit();

    /// <summary>Opens the onboarding quiz again ("Setup style", SPEC §7).</summary>
    void ShowOnboarding();
}

public enum UpdateStateKind
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToInstall,
    Failed,

    /// <summary>Running from a build that cannot self-update (portable zip, dev build).</summary>
    NotSupported,
}

public sealed record UpdateState(UpdateStateKind Kind, string? Version = null, int Percent = 0);

public interface IUpdateService
{
    UpdateState State { get; }

    event Action? Changed;

    Task CheckAsync(CancellationToken cancellationToken = default);

    void ApplyAndRestart();
}

public interface IDiagnosticsExporter
{
    /// <summary>Writes logs + settings + monitor info (no process lists, SPEC §7) to a zip and returns its path.</summary>
    Task<string> ExportAsync(CancellationToken cancellationToken = default);
}

public sealed record RestoreOutcome(bool NothingToRestore, int MonitorsRestored, string? Note);

public interface IRestoreService
{
    Task<RestoreOutcome> RestoreOriginalAsync(CancellationToken cancellationToken = default);
}

public interface IAppInfo
{
    string Version { get; }

    string LogsDirectory { get; }

    string DataDirectory { get; }

    /// <summary>Generated third-party notices (SPEC §10).</summary>
    string ThirdPartyNotices { get; }
}

public interface IAppMaintenance
{
    /// <summary>Resets settings and removes downloaded content ("reset app"), keeping the user's own images unless asked.</summary>
    Task ResetAsync(bool removeUserImages, CancellationToken cancellationToken = default);
}

/// <summary>Which installed games were found locally (FR-DET-9), cached and refreshed off the UI thread.</summary>
public interface IInstalledGamesProvider
{
    Task<IReadOnlySet<string>> GetInstalledGameIdsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Shows a tray notification. Implementations must never block and never show more than the caller asks for.</summary>
public interface INotifier
{
    void Show(string title, string body, Action? onClick = null);
}
