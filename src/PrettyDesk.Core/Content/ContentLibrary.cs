using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Imaging;

namespace PrettyDesk.Core.Content;

public enum PackStateKind
{
    NotDownloaded,
    Downloading,
    Ready,
    Failed,
}

public sealed record PackProgress(string PackId, PackStateKind State, double Fraction);

public sealed record ContentLibraryOptions(
    string? BundledContentDirectory,
    int MaxConcurrentDownloads = 2,
    TimeSpan? FailureCooldown = null);

/// <summary>
/// Everything about wallpaper files on disk: resolves wallpapers to local variants (downloaded packs, bundled starter set,
/// the user's images), and downloads only the variants the attached monitors need (FR-CON-3/4/5/6).
/// </summary>
public sealed partial class ContentLibrary : IContentLibrary, IContentBrowser, IDisposable
{
    private readonly ICatalogProvider _catalog;
    private readonly PackStore _packs;
    private readonly UserImageStore _user;
    private readonly FileDownloader _downloader;
    private readonly ContentLibraryOptions _options;
    private readonly Func<IReadOnlySet<string>> _protectedPacks;
    private readonly Func<long> _capBytes;
    private readonly TimeProvider _time;
    private readonly ILogger<ContentLibrary> _logger;
    private readonly SemaphoreSlim _downloadGate;
    private readonly ConcurrentDictionary<string, Task> _inflight = new();
    private readonly ConcurrentDictionary<string, PackProgress> _states = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failedAt = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _touched = new();
    private readonly ConcurrentDictionary<string, bool> _bundledHashMatches = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _indexLock = new();
    private Dictionary<string, (PackEntry Pack, WallpaperEntry Wallpaper)>? _index;

    public ContentLibrary(
        ICatalogProvider catalog,
        PackStore packs,
        UserImageStore user,
        FileDownloader downloader,
        ContentLibraryOptions options,
        Func<IReadOnlySet<string>> protectedPacks,
        Func<long> capBytes,
        TimeProvider time,
        ILogger<ContentLibrary> logger)
    {
        _catalog = catalog;
        _packs = packs;
        _user = user;
        _downloader = downloader;
        _options = options;
        _protectedPacks = protectedPacks;
        _capBytes = capBytes;
        _time = time;
        _logger = logger;
        _downloadGate = new SemaphoreSlim(options.MaxConcurrentDownloads);
        catalog.Changed += InvalidateIndex;
    }

    public event Action<string>? PackChanged;

    public event Action<PackProgress>? ProgressChanged;

    public event Action<string, Exception>? DownloadFailed;

    public UserImageStore UserImages => _user;

    // ---- lookup ---------------------------------------------------------------------------------------------------

    public IReadOnlyList<string> GetWallpaperIds(string packId)
    {
        if (packId == Orchestration.ContentIds.UserPackId)
        {
            return _user.List().Select(i => i.Id).ToList();
        }

        return _catalog.Current.FindPack(packId)?.Wallpapers.Select(w => w.Id).ToList() ?? [];
    }

    public WallpaperAsset? TryGetAsset(string wallpaperId)
    {
        if (UserImageStore.IsUserId(wallpaperId))
        {
            var image = _user.TryGet(wallpaperId);
            return image is null
                ? null
                : new WallpaperAsset(
                    image.Id,
                    Orchestration.ContentIds.UserPackId,
                    image.Id,
                    Tones.Mid,
                    FocalPoint.Center,
                    new Dictionary<string, LocalVariant> { [Variants.User] = new LocalVariant(Variants.User, image.Path, image.Width, image.Height) },
                    System.IO.Path.GetFileNameWithoutExtension(image.Path),
                    IsUserImage: true);
        }

        if (Index().TryGetValue(wallpaperId, out var entry))
        {
            return ResolveCatalogAsset(entry.Pack, entry.Wallpaper);
        }

        return ResolveLooseAsset(wallpaperId);
    }

    /// <summary>Local thumbnail path for a wallpaper (downloaded or bundled), or null when none is on disk.</summary>
    public string? GetThumbnailPath(string wallpaperId)
    {
        if (!Index().TryGetValue(wallpaperId, out var entry) || entry.Wallpaper.Thumb is not { } thumb)
        {
            return null;
        }

        return LocateFile(entry.Pack, thumb.Path);
    }

    public string? GetPreviewImagePath(string wallpaperId)
    {
        if (GetThumbnailPath(wallpaperId) is { } thumb)
        {
            return thumb;
        }

        // No thumbnail: the smallest variant on disk is the cheapest thing to decode at thumbnail size.
        return TryGetAsset(wallpaperId)?.Variants.Values.OrderBy(v => (long)v.Width * v.Height).FirstOrDefault()?.Path;
    }

    public IReadOnlyList<UserImage> ListUserImages() => _user.List();

    public ImportResult ImportUserImage(string sourcePath)
    {
        var result = _user.Import(sourcePath);
        if (result.Ok)
        {
            PackChanged?.Invoke(Orchestration.ContentIds.UserPackId);
        }

        return result;
    }

    public bool RemoveUserImage(string id)
    {
        var removed = _user.Remove(id);
        if (removed)
        {
            PackChanged?.Invoke(Orchestration.ContentIds.UserPackId);
        }

        return removed;
    }

    public long StorageUsageBytes() => _packs.UsageBytes();

    public long ClearDownloaded(IReadOnlySet<string>? keepPackIds = null)
    {
        var freed = _packs.ClearAll(keepPackIds);
        _states.Clear();
        foreach (var packId in _catalog.Current.Packs.Select(p => p.Id))
        {
            PackChanged?.Invoke(packId);
        }

        return freed;
    }

    public PackProgress GetPackState(string packId)
    {
        if (_states.TryGetValue(packId, out var state))
        {
            return state;
        }

        var pack = _catalog.Current.FindPack(packId);
        if (pack is null || pack.Wallpapers.Count == 0)
        {
            return new PackProgress(packId, PackStateKind.NotDownloaded, 0);
        }

        var ready = pack.Wallpapers.Count(w => TryGetAsset(w.Id) is not null);
        return new PackProgress(packId, ready == pack.Wallpapers.Count ? PackStateKind.Ready : PackStateKind.NotDownloaded, (double)ready / pack.Wallpapers.Count);
    }

    // ---- downloading ----------------------------------------------------------------------------------------------

    public void RequestPack(string packId, IReadOnlyList<MonitorInfo> monitors) => _ = EnsurePackAsync(packId, monitors, _cts.Token);

    /// <summary>Downloads whatever the monitors need for a pack. Concurrent calls for the same pack share one run.</summary>
    public Task EnsurePackAsync(string packId, IReadOnlyList<MonitorInfo> monitors, CancellationToken cancellationToken)
    {
        var pack = _catalog.Current.FindPack(packId);
        if (pack is null || pack.Wallpapers.Count == 0)
        {
            return Task.CompletedTask;
        }

        var cooldown = _options.FailureCooldown ?? TimeSpan.FromMinutes(2);
        if (_failedAt.TryGetValue(packId, out var failed) && _time.GetUtcNow() - failed < cooldown)
        {
            return Task.CompletedTask;
        }

        return _inflight.GetOrAdd(packId, _ => RunAsync(pack, monitors.ToList(), cancellationToken));
    }

    /// <summary>Background prefetch for installed + enabled games (FR-CON-4).</summary>
    public void Prefetch(IEnumerable<string> packIds, IReadOnlyList<MonitorInfo> monitors)
    {
        foreach (var packId in packIds.Distinct(StringComparer.Ordinal))
        {
            RequestPack(packId, monitors);
        }
    }

    public void Dispose()
    {
        _catalog.Changed -= InvalidateIndex;
        _cts.Cancel();
        _cts.Dispose();
        _downloadGate.Dispose();
    }

    private async Task RunAsync(PackEntry pack, List<MonitorInfo> monitors, CancellationToken ct)
    {
        try
        {
            await Task.Yield();
            Publish(new PackProgress(pack.Id, PackStateKind.Downloading, 0));

            var plans = pack.Wallpapers.Select(w => (Wallpaper: w, Files: FilesNeeded(pack, w, monitors))).ToList();
            var totalBytes = Math.Max(1, plans.Sum(p => p.Files.Sum(f => f.File.Bytes)));
            long done = 0;
            var lastReport = DateTimeOffset.MinValue;
            var progress = new SyncProgress(bytes =>
            {
                var current = Interlocked.Add(ref done, bytes);
                var now = _time.GetUtcNow();
                if (now - lastReport > TimeSpan.FromMilliseconds(200))
                {
                    lastReport = now;
                    Publish(new PackProgress(pack.Id, PackStateKind.Downloading, Math.Min(0.99, (double)current / totalBytes)));
                }
            });

            var baseUri = new Uri(_catalog.Current.ContentBaseUrl);
            foreach (var (_, files) in plans)
            {
                // One wallpaper at a time so the first usable wallpaper lands as early as possible.
                await Task.WhenAll(files.Select(f => DownloadOneAsync(pack, baseUri, f.File, progress, ct)));
                if (files.Count > 0)
                {
                    PackChanged?.Invoke(pack.Id);
                }
            }

            CompleteSwap(pack);
            _failedAt.TryRemove(pack.Id, out _);
            Publish(new PackProgress(pack.Id, PackStateKind.Ready, 1));
            _packs.Touch(pack.Id);
            _packs.EvictToCap(_capBytes(), _protectedPacks());
            PackChanged?.Invoke(pack.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
#pragma warning disable CA1031 // A failed pack download must never crash the tray; it is reported through DownloadFailed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _failedAt[pack.Id] = _time.GetUtcNow();
            Publish(new PackProgress(pack.Id, PackStateKind.Failed, 0));
            LogPackFailed(pack.Id, ex);
            DownloadFailed?.Invoke(pack.Id, ex);
        }
        finally
        {
            _inflight.TryRemove(pack.Id, out _);
        }
    }

    private async Task DownloadOneAsync(PackEntry pack, Uri baseUri, FileRef file, IProgress<long> progress, CancellationToken ct)
    {
        await _downloadGate.WaitAsync(ct);
        try
        {
            var destination = _packs.FilePath(pack.Id, pack.Version, file.Path);
            await _downloader.DownloadAsync(new Uri(baseUri, file.Path), destination, file.Sha256, progress, ct);
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    /// <summary>Files missing locally that the attached monitors (and the library UI) need for one wallpaper.</summary>
    private List<(string Key, FileRef File)> FilesNeeded(PackEntry pack, WallpaperEntry wallpaper, IReadOnlyList<MonitorInfo> monitors)
    {
        var wanted = new Dictionary<string, FileRef>(StringComparer.Ordinal);
        var ratios = wallpaper.Variants.Where(v => Variants.IsKnown(v.Key)).ToDictionary(v => v.Key, v => Variants.Find(v.Key)!.Ratio);
        foreach (var monitor in monitors)
        {
            if (VariantSelector.Select(monitor.PixelWidth, monitor.PixelHeight, ratios) is { } choice)
            {
                wanted[choice.Key] = wallpaper.Variants[choice.Key];
            }
        }

        var files = wanted.Select(kv => (kv.Key, File: kv.Value)).ToList();
        if (wallpaper.Thumb is { } thumb)
        {
            files.Add(("thumb", thumb));
        }

        return files.Where(f => !HasCurrentCopy(pack, f.File)).ToList();
    }

    /// <summary>
    /// True when the file is already on disk in its <em>current</em> form: in this pack version's folder, or bundled with a
    /// matching hash. An older version's copy does not count, otherwise a pack update would never fetch the new art.
    /// </summary>
    private bool HasCurrentCopy(PackEntry pack, FileRef file)
    {
        if (File.Exists(_packs.FilePath(pack.Id, pack.Version, file.Path)))
        {
            return true;
        }

        if (_options.BundledContentDirectory is not { } bundled)
        {
            return false;
        }

        var path = System.IO.Path.Combine(bundled, System.IO.Path.GetFileName(file.Path));
        return File.Exists(path) && _bundledHashMatches.GetOrAdd(path + "|" + file.Sha256, _ => FileDownloader.HashFileAsync(path, CancellationToken.None).GetAwaiter().GetResult() == file.Sha256);
    }

    private void CompleteSwap(PackEntry pack)
    {
        var current = _packs.VersionDirectory(pack.Id, pack.Version);
        if (!Directory.Exists(current))
        {
            return;
        }

        var newNames = Directory.GetFiles(current).Select(System.IO.Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        foreach (var (version, directory) in _packs.Versions(pack.Id))
        {
            if (version == pack.Version)
            {
                continue;
            }

            // Keep files the new version doesn't replace: a wallpaper dropped from the catalog stays usable locally (FR-CON-5).
            foreach (var file in Directory.GetFiles(directory))
            {
                if (newNames.Contains(System.IO.Path.GetFileName(file)))
                {
                    File.Delete(file);
                }
            }

            if (Directory.GetFiles(directory).Length == 0)
            {
                Directory.Delete(directory);
            }
        }
    }

    // ---- resolution helpers ---------------------------------------------------------------------------------------

    private WallpaperAsset? ResolveCatalogAsset(PackEntry pack, WallpaperEntry wallpaper)
    {
        var variants = new Dictionary<string, LocalVariant>();
        var hashInput = new StringBuilder();
        foreach (var (key, file) in wallpaper.Variants.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            if (LocateFile(pack, file.Path) is { } path)
            {
                variants[key] = new LocalVariant(key, path, file.W, file.H);
                hashInput.Append(key).Append(':').Append(file.Sha256).Append(';');
            }
        }

        if (variants.Count == 0)
        {
            return null;
        }

        Touch(pack.Id);
        return new WallpaperAsset(wallpaper.Id, pack.Id, wallpaper.Title, wallpaper.Tone, wallpaper.Focal, variants, Hash(hashInput.ToString()));
    }

    private WallpaperAsset? ResolveLooseAsset(string wallpaperId)
    {
        var variants = new Dictionary<string, LocalVariant>();
        foreach (var spec in Variants.All)
        {
            var file = _packs.FindLooseFile($"{wallpaperId}_{spec.Key}.jpg");
            if (file is not null)
            {
                variants[spec.Key] = new LocalVariant(spec.Key, file, spec.Width, spec.Height);
            }
        }

        return variants.Count == 0
            ? null
            : new WallpaperAsset(wallpaperId, "removed", wallpaperId, Tones.Mid, FocalPoint.Center, variants, Hash(string.Join(';', variants.Values.Select(v => v.Path))));
    }

    /// <summary>Current version first, then older versions, then the bundled starter set.</summary>
    private string? LocateFile(PackEntry pack, string catalogPath)
    {
        var current = _packs.FilePath(pack.Id, pack.Version, catalogPath);
        if (File.Exists(current))
        {
            return current;
        }

        var name = System.IO.Path.GetFileName(catalogPath);
        foreach (var (_, directory) in _packs.Versions(pack.Id))
        {
            var older = System.IO.Path.Combine(directory, name);
            if (File.Exists(older))
            {
                return older;
            }
        }

        if (_options.BundledContentDirectory is { } bundled)
        {
            var path = System.IO.Path.Combine(bundled, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private Dictionary<string, (PackEntry Pack, WallpaperEntry Wallpaper)> Index()
    {
        lock (_indexLock)
        {
            if (_index is not null)
            {
                return _index;
            }

            var index = new Dictionary<string, (PackEntry, WallpaperEntry)>(StringComparer.Ordinal);
            foreach (var pack in _catalog.Current.Packs)
            {
                foreach (var wallpaper in pack.Wallpapers)
                {
                    index[wallpaper.Id] = (pack, wallpaper);
                }
            }

            _index = index;
            return index;
        }
    }

    private void InvalidateIndex()
    {
        lock (_indexLock)
        {
            _index = null;
        }
    }

    private void Touch(string packId)
    {
        var now = _time.GetUtcNow();
        if (!_touched.TryGetValue(packId, out var last) || now - last > TimeSpan.FromHours(1))
        {
            _touched[packId] = now;
            _packs.Touch(packId);
        }
    }

    private void Publish(PackProgress progress)
    {
        _states[progress.PackId] = progress;
        ProgressChanged?.Invoke(progress);
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed class SyncProgress(Action<long> onReport) : IProgress<long>
    {
        public void Report(long value) => onReport(value);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Downloading pack {PackId} failed; the current wallpaper stays")]
    private partial void LogPackFailed(string packId, Exception ex);
}
