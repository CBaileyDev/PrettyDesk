using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.Tests.Support;

internal sealed class InlineDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class FakeController : IWallpaperController
{
    public OrchestratorStatus Status { get; set; } = new();
    public List<string> Calls { get; } = [];

    public event Action<OrchestratorStatus>? StatusChanged;

    public void Raise(OrchestratorStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    public void NextWallpaper() => Calls.Add("next");

    public void Pause(TimeSpan? duration) => Calls.Add(duration is null ? "pause:forever" : $"pause:{duration.Value.TotalMinutes}");

    public void Resume() => Calls.Add("resume");

    public void Preview(string wallpaperId, TimeSpan? duration = null) => Calls.Add("preview:" + wallpaperId);

    public void CancelPreview() => Calls.Add("cancel-preview");
}

internal sealed class FakeMonitorProvider : IMonitorProvider
{
    public List<MonitorInfo> Monitors { get; set; } = [new MonitorInfo("m1", 0, 0, 1920, 1080, true)];

    public event Action? Changed;

    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors.ToList();

    public void Raise() => Changed?.Invoke();
}

internal sealed class FakeConflicts : IEnvironmentConflictSource
{
    public EnvironmentConflict Value { get; set; }

    public EnvironmentConflict Detect() => Value;
}

internal sealed class FakeContentBrowser : IContentBrowser
{
    public Dictionary<string, PackDownloadPlan> Plans { get; } = [];
    public PackDownloadPlan GetDownloadPlan(string packId, IReadOnlyList<MonitorInfo> monitors) => Plans.GetValueOrDefault(packId) ?? new(true, true, 1024, ["16x9"], false);
    public Dictionary<string, string> Previews { get; } = [];
    public Dictionary<string, PackProgress> States { get; } = [];
    public List<UserImage> Images { get; } = [];
    public List<string> Requested { get; } = [];
    public long Usage { get; set; }
    public long Freed { get; set; }
    public ImportResult? NextImport { get; set; }
    public List<string> ImportCalls { get; } = [];

    public event Action<PackProgress>? ProgressChanged;

    public event Action<string>? PackChanged;

    public event Action<string, Exception>? DownloadFailed;

    public PackProgress GetPackState(string packId) => States.GetValueOrDefault(packId) ?? new PackProgress(packId, PackStateKind.NotDownloaded, 0);

    public string? GetPreviewImagePath(string wallpaperId) => Previews.GetValueOrDefault(wallpaperId);

    public IReadOnlyList<UserImage> ListUserImages() => Images.ToList();

    public ImportResult ImportUserImage(string sourcePath)
    {
        ImportCalls.Add(sourcePath);
        var result = NextImport ?? new ImportResult(new UserImage("user:" + Path.GetFileName(sourcePath), sourcePath, 2000, 1000), ImportFailure.None, null);
        if (result.Ok)
        {
            Images.Add(result.Image!);
        }

        return result;
    }

    public bool RemoveUserImage(string id) => Images.RemoveAll(i => i.Id == id) > 0;

    public long StorageUsageBytes() => Usage;

    public long ClearDownloaded(IReadOnlySet<string>? keepPackIds = null)
    {
        Usage = 0;
        return Freed;
    }

    public void RequestPack(string packId, IReadOnlyList<MonitorInfo> monitors) => Requested.Add(packId);

    public void RaisePackChanged(string packId) => PackChanged?.Invoke(packId);

    public void RaiseProgress(PackProgress progress)
    {
        States[progress.PackId] = progress;
        ProgressChanged?.Invoke(progress);
    }

    public void RaiseFailed(string packId) => DownloadFailed?.Invoke(packId, new InvalidOperationException("x"));
}

internal sealed class FakeSettingsProvider : ISettingsProvider
{
    public AppSettings Current { get; private set; } = new();
    public int UpdateCount { get; private set; }

    public event Action? Changed;

    public void Update(Action<AppSettings> mutate)
    {
        var clone = SettingsService.Clone(Current);
        mutate(clone);
        Current = clone;
        UpdateCount++;
        Changed?.Invoke();
    }

    /// <summary>Simulates another part of the app changing settings.</summary>
    public void ChangeExternally(Action<AppSettings> mutate) => Update(mutate);
}

internal sealed class FakeDetectionFeed : IDetectionFeed
{
    public event Action<PrettyDesk.Core.Detection.DetectionObservation>? Observed;

    public void Raise(string? exe, params string[] matches) =>
        Observed?.Invoke(new PrettyDesk.Core.Detection.DetectionObservation(exe, matches));
}

internal sealed class FakeCatalogProvider(PrettyDesk.Core.Catalog.CatalogDocument document) : ICatalogProvider
{
    public PrettyDesk.Core.Catalog.CatalogDocument Current { get; set; } = document;

    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}

internal sealed class FakeLibrary : IContentLibrary
{
    public Dictionary<string, List<string>> Packs { get; } = [];
    public HashSet<string> Available { get; } = [];

    /// <summary>Tone per available wallpaper; anything not listed is dark, the common case in these tests.</summary>
    public Dictionary<string, string> ToneById { get; } = [];

    public event Action<string>? PackChanged;

    public IReadOnlyList<string> GetWallpaperIds(string packId) => Packs.GetValueOrDefault(packId) ?? [];

    public PrettyDesk.Core.Catalog.WallpaperAsset? TryGetAsset(string wallpaperId) =>
        Available.Contains(wallpaperId)
            ? new PrettyDesk.Core.Catalog.WallpaperAsset(wallpaperId, "p", wallpaperId, ToneById.GetValueOrDefault(wallpaperId, "dark"), PrettyDesk.Core.Catalog.FocalPoint.Center, new Dictionary<string, PrettyDesk.Core.Catalog.LocalVariant>(), "h")
            : null;

    public void RequestPack(string packId, IReadOnlyList<MonitorInfo> monitors)
    {
    }

    public void Raise(string packId) => PackChanged?.Invoke(packId);
}

internal sealed class FakeFilePicker : PrettyDesk.Presentation.Services.IFilePicker
{
    public List<string> Images { get; set; } = [];
    public string? Executable { get; set; }

    public IReadOnlyList<string> PickImages() => Images;

    public string? PickExecutable() => Executable;
}

internal sealed class FakeRunningApps : PrettyDesk.Presentation.Services.IRunningAppsProvider
{
    public List<PrettyDesk.Presentation.Services.RunningApp> Apps { get; set; } = [];

    public IReadOnlyList<PrettyDesk.Presentation.Services.RunningApp> GetWindowedApps() => Apps;
}

internal sealed class FakeInstalled : PrettyDesk.Presentation.Services.IInstalledGamesProvider
{
    public HashSet<string> Ids { get; set; } = [];
    public bool Throw { get; set; }

    public Task<IReadOnlySet<string>> GetInstalledGameIdsAsync(CancellationToken cancellationToken = default) =>
        Throw ? throw new IOException("scan failed") : Task.FromResult<IReadOnlySet<string>>(Ids);
}
