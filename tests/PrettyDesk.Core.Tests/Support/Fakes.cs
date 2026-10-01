using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Settings;

namespace PrettyDesk.Core.Tests.Support;

internal sealed class FakeMonitors : IMonitorProvider
{
    public List<MonitorInfo> Monitors { get; set; } = [Mon("M1", 1920, 1080, primary: true)];

    public event Action? Changed;

    public static MonitorInfo Mon(string id, int w, int h, bool primary = false) => new(id, 0, 0, w, h, primary);

    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors.ToList();

    public void RaiseChanged() => Changed?.Invoke();
}

internal sealed class FakeSetter : IWallpaperSetter
{
    public List<(string Monitor, string Path)> Calls { get; } = [];
    public Dictionary<string, string?> Current { get; } = [];
    public bool Fail { get; set; }

    public Task SetAsync(string monitorId, string path, CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("COM failure");
        }

        Calls.Add((monitorId, path));
        Current[monitorId] = path;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string monitorId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Current.GetValueOrDefault(monitorId));
}

internal sealed class FakeRenderer : IWallpaperRenderer
{
    public Task<string> RenderAsync(WallpaperAsset asset, MonitorInfo monitor, CancellationToken cancellationToken = default) =>
        Task.FromResult($"/cache/{asset.Id}_{monitor.PixelWidth}x{monitor.PixelHeight}.png");
}

internal sealed class FakeContent : IContentLibrary
{
    public Dictionary<string, List<string>> Packs { get; } = [];
    public Dictionary<string, WallpaperAsset> Assets { get; } = [];
    public List<string> Requested { get; } = [];

    public event Action<string>? PackChanged;

    public void AddPack(string packId, string tone = Tones.Dark, bool downloaded = true, params string[] ids)
    {
        Packs[packId] = ids.ToList();
        if (downloaded)
        {
            foreach (var id in ids)
            {
                MakeAvailable(packId, id, tone);
            }
        }
    }

    public void MakeAvailable(string packId, string id, string tone = Tones.Dark) =>
        Assets[id] = new WallpaperAsset(id, packId, id, tone, FocalPoint.Center, new Dictionary<string, LocalVariant>(), "hash-" + id);

    public void Download(string packId, string tone = Tones.Dark)
    {
        foreach (var id in Packs[packId])
        {
            MakeAvailable(packId, id, tone);
        }

        PackChanged?.Invoke(packId);
    }

    public IReadOnlyList<string> GetWallpaperIds(string packId) => Packs.GetValueOrDefault(packId) ?? [];

    public WallpaperAsset? TryGetAsset(string wallpaperId) => Assets.GetValueOrDefault(wallpaperId);

    public void RequestPack(string packId, IReadOnlyList<MonitorInfo> monitors) => Requested.Add(packId);
}

internal sealed class FakeSystem : ISystemState
{
    public bool IsLightTheme { get; set; }
    public bool IsBatterySaverOn { get; set; }
    public bool IsWallpaperPolicyLocked { get; set; }

    public event Action? Changed;

    public void RaiseChanged() => Changed?.Invoke();
}

internal sealed class FakeCatalog(CatalogDocument document) : ICatalogProvider
{
    public CatalogDocument Current { get; set; } = document;

    public event Action? Changed;

    public void RaiseChanged() => Changed?.Invoke();
}

internal sealed class FakeSettings : ISettingsProvider
{
    public AppSettings Current { get; private set; } = new();

    public event Action? Changed;

    public void Update(Action<AppSettings> mutate)
    {
        var clone = SettingsService.Clone(Current);
        mutate(clone);
        Current = clone;
        Changed?.Invoke();
    }
}
