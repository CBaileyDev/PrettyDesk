using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Windows;

namespace PrettyDesk.App.Services;

public sealed class AppInfoService : IAppInfo
{
    private readonly AppPaths _paths;

    public AppInfoService(AppPaths paths) => _paths = paths;

    public string Version => AppConstants.DisplayVersion;

    public string LogsDirectory => _paths.Logs;

    public string DataDirectory => _paths.Root;

    public string ThirdPartyNotices
    {
        get
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("THIRD-PARTY-NOTICES.txt");
            if (stream is null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}

/// <summary>
/// "Export diagnostics" (SPEC §7.4): logs + settings + monitor info + versions, and never a process list. Logs are already
/// scrubbed of the user name at write time.
/// </summary>
public sealed class DiagnosticsExporter : IDiagnosticsExporter
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly AppPaths _paths;
    private readonly IMonitorProvider _monitors;
    private readonly ISettingsProvider _settings;
    private readonly TimeProvider _time;

    public DiagnosticsExporter(AppPaths paths, IMonitorProvider monitors, ISettingsProvider settings, TimeProvider time)
    {
        _paths = paths;
        _monitors = monitors;
        _settings = settings;
        _time = time;
    }

    public Task<string> ExportAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!Directory.Exists(folder))
        {
            folder = _paths.Root;
        }

        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var target = Path.Combine(folder, $"PrettyDesk-diagnostics-{stamp}.zip");
        using (var zip = ZipFile.Open(target, ZipArchiveMode.Create))
        {
            if (Directory.Exists(_paths.Logs))
            {
                foreach (var log in Directory.GetFiles(_paths.Logs, "*.log"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var source = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var entry = zip.CreateEntry("logs/" + Path.GetFileName(log)).Open();
                    source.CopyTo(entry);
                }
            }

            WriteJson(zip, "settings.json", SettingsService.Clone(_settings.Current), SettingsJsonContext.Default.AppSettings);
            var info = new
            {
                version = AppConstants.DisplayVersion,
                os = Environment.OSVersion.VersionString,
                architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                monitors = _monitors.GetMonitors().Select(m => new { m.PixelWidth, m.PixelHeight, m.IsPrimary }).ToList(),
            };
            using var infoEntry = zip.CreateEntry("system.json").Open();
            JsonSerializer.Serialize(infoEntry, info, Indented);
        }

        return target;
    }, cancellationToken);

    private static void WriteJson<T>(ZipArchive zip, string name, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        using var entry = zip.CreateEntry(name).Open();
        JsonSerializer.Serialize(entry, value, typeInfo);
    }
}

public sealed class RestoreService : IRestoreService
{
    private readonly WallpaperBackupService _backup;
    private readonly IWallpaperController _controller;

    public RestoreService(WallpaperBackupService backup, IWallpaperController controller)
    {
        _backup = backup;
        _controller = controller;
    }

    public async Task<RestoreOutcome> RestoreOriginalAsync(CancellationToken cancellationToken = default)
    {
        // Pause first so PrettyDesk does not immediately apply its own wallpaper over the restored one.
        _controller.Pause(null);
        var result = await _backup.RestoreAsync(cancellationToken);
        return new RestoreOutcome(result.NothingToRestore, result.MonitorsRestored, result.Note);
    }
}

/// <summary>"Reset PrettyDesk": defaults for every setting, no downloaded content, optionally no user images. The original-wallpaper backup is never touched.</summary>
public sealed partial class AppMaintenance : IAppMaintenance
{
    private readonly ISettingsProvider _settings;
    private readonly IContentBrowser _content;
    private readonly AppStateService _state;
    private readonly AppPaths _paths;
    private readonly IAppController _app;
    private readonly ILogger<AppMaintenance> _logger;

    public AppMaintenance(ISettingsProvider settings, IContentBrowser content, AppStateService state, AppPaths paths, IAppController app, ILogger<AppMaintenance> logger)
    {
        _settings = settings;
        _content = content;
        _state = state;
        _paths = paths;
        _app = app;
        _logger = logger;
    }

    public async Task ResetAsync(bool removeUserImages, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            _content.ClearDownloaded();
            if (removeUserImages)
            {
                foreach (var image in _content.ListUserImages())
                {
                    _content.RemoveUserImage(image.Id);
                }
            }

            if (Directory.Exists(_paths.RenderCache))
            {
                foreach (var file in Directory.GetFiles(_paths.RenderCache))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                        // an applied wallpaper may be locked; it is replaced on the next apply
                    }
                }
            }
        }, cancellationToken);

        _state.Current.Rotation.Clear();
        _state.SaveNow();
        var fresh = new AppSettings();
        _settings.Update(s =>
        {
            s.General = fresh.General;
            s.Detection = fresh.Detection;
            s.Default = fresh.Default;
            s.Games = fresh.Games;
            s.CustomGames = removeUserImages ? fresh.CustomGames : s.CustomGames;
            s.Monitors = fresh.Monitors;
            s.Content = fresh.Content;
            s.Notifications = fresh.Notifications;
        });
        LogReset();
        _app.ShowOnboarding();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "PrettyDesk was reset by the user")]
    private partial void LogReset();
}
