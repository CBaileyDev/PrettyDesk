namespace PrettyDesk.Core.Content;

/// <summary>
/// On-disk layout of downloaded packs: <c>packs/{packId}/v{version}/{file}</c> (FR-CON-6), plus usage accounting, LRU
/// eviction by pack and "clear downloaded content".
/// </summary>
public sealed class PackStore
{
    private const string UsedMarker = ".used";

    private readonly string _root;

    public PackStore(string root) => _root = root;

    public string Root => _root;

    public string VersionDirectory(string packId, int version) => Path.Combine(_root, Sanitize(packId), $"v{version}");

    public string FilePath(string packId, int version, string catalogPath) =>
        Path.Combine(VersionDirectory(packId, version), Path.GetFileName(catalogPath));

    /// <summary>
    /// Marks a pack as recently used so LRU eviction keeps it. The stamp is the write time of a <c>.used</c> marker file we
    /// control: file-system access times are unreliable (NTFS mostly disables them; Linux relatime refreshes them on a
    /// directory read).
    /// </summary>
    public void Touch(string packId)
    {
        var directory = Path.Combine(_root, Sanitize(packId));
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var marker = Path.Combine(directory, UsedMarker);
            if (File.Exists(marker))
            {
                File.SetLastWriteTimeUtc(marker, DateTime.UtcNow);
            }
            else
            {
                File.WriteAllBytes(marker, []);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort
        }
    }

    public DateTime LastUsedUtc(string packId)
    {
        var directory = Path.Combine(_root, Sanitize(packId));
        var marker = Path.Combine(directory, UsedMarker);
        return File.Exists(marker) ? File.GetLastWriteTimeUtc(marker) : Directory.GetCreationTimeUtc(directory);
    }

    /// <summary>Every version directory of a pack, newest first.</summary>
    public IReadOnlyList<(int Version, string Directory)> Versions(string packId)
    {
        var directory = Path.Combine(_root, Sanitize(packId));
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetDirectories(directory, "v*")
            .Select(d => (Ok: int.TryParse(Path.GetFileName(d).AsSpan(1), out var v), Version: v, Directory: d))
            .Where(x => x.Ok)
            .OrderByDescending(x => x.Version)
            .Select(x => (x.Version, x.Directory))
            .ToList();
    }

    /// <summary>Finds a downloaded file by name in any version of any pack (a wallpaper removed from the catalog stays usable, FR-CON-5).</summary>
    public string? FindLooseFile(string fileName)
    {
        if (!Directory.Exists(_root))
        {
            return null;
        }

        return Directory.EnumerateFiles(_root, fileName, SearchOption.AllDirectories).FirstOrDefault();
    }

    public IEnumerable<string> FindLooseFilesByPrefix(string prefix) =>
        Directory.Exists(_root) ? Directory.EnumerateFiles(_root, prefix + "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".part", StringComparison.Ordinal)) : [];

    public long UsageBytes() =>
        Directory.Exists(_root) ? new DirectoryInfo(_root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    public IReadOnlyList<string> InstalledPackIds() =>
        Directory.Exists(_root) ? Directory.GetDirectories(_root).Select(d => Path.GetFileName(d)).ToList() : [];

    /// <summary>Deletes everything downloaded ("Clear downloaded content"). Returns bytes freed.</summary>
    public long ClearAll(IReadOnlySet<string>? keepPackIds = null)
    {
        var freed = 0L;
        foreach (var pack in InstalledPackIds())
        {
            if (keepPackIds is not null && keepPackIds.Contains(pack))
            {
                continue;
            }

            freed += DeletePack(pack);
        }

        return freed;
    }

    /// <summary>Evicts least-recently-used packs until usage is under <paramref name="capBytes"/>, never touching protected packs.</summary>
    public long EvictToCap(long capBytes, IReadOnlySet<string> protectedPackIds)
    {
        var freed = 0L;
        var usage = UsageBytes();
        if (usage <= capBytes)
        {
            return 0;
        }

        var candidates = InstalledPackIds()
            .Where(p => !protectedPackIds.Contains(p))
            .Select(p => (Pack: p, Access: LastUsedUtc(p)))
            .OrderBy(x => x.Access)
            .ToList();

        foreach (var (pack, _) in candidates)
        {
            if (usage - freed <= capBytes)
            {
                break;
            }

            freed += DeletePack(pack);
        }

        return freed;
    }

    /// <summary>Removes every version of a pack except <paramref name="keepVersion"/> (called after an update swap completes).</summary>
    public void DeleteOldVersions(string packId, int keepVersion)
    {
        foreach (var (version, directory) in Versions(packId))
        {
            if (version != keepVersion)
            {
                TryDelete(directory);
            }
        }
    }

    /// <summary>Removes interrupted <c>.part</c> files older than a day.</summary>
    public void CleanStalePartials(TimeProvider time)
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        var cutoff = time.GetUtcNow().UtcDateTime - TimeSpan.FromDays(1);
        foreach (var file in Directory.EnumerateFiles(_root, "*.part", SearchOption.AllDirectories))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                TryDelete(file);
            }
        }
    }

    private long DeletePack(string packId)
    {
        var directory = Path.Combine(_root, packId);
        var size = Directory.Exists(directory) ? new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
        TryDelete(directory);
        return size;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Files in use (e.g. applied wallpaper) are skipped; the next eviction pass retries.
        }
    }

    /// <summary>One safe directory name per pack id. An id made only of dots (".", "..") would resolve to the store root or its parent.</summary>
    private static string Sanitize(string id)
    {
        var safe = string.Concat(id.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '_'));
        return safe.Length == 0 || safe.All(c => c == '.') ? new string('_', Math.Max(1, safe.Length)) : safe;
    }
}
