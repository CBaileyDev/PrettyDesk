using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PrettyDesk.Core.Imaging;

/// <summary>
/// Per-monitor PNG render cache (FR-APPLY-3). File names embed the wallpaper, variant, exact pixel size and a content
/// hash so that every distinct image has a distinct path (Windows may not refresh when a path is reused).
/// </summary>
public sealed partial class RenderCache
{
    public const long DefaultMaxBytes = 750L * 1024 * 1024;

    private readonly string _directory;
    private readonly Func<IReadOnlySet<string>> _appliedPaths;
    private readonly long _maxBytes;
    private readonly ILogger? _logger;
    private readonly object _gate = new();

    public RenderCache(string directory, Func<IReadOnlySet<string>> appliedPaths, long maxBytes = DefaultMaxBytes, ILogger? logger = null)
    {
        _directory = directory;
        _appliedPaths = appliedPaths;
        _maxBytes = maxBytes;
        _logger = logger;
    }

    public string Directory => _directory;

    public string PathFor(string wallpaperId, string variantKey, int width, int height, string contentHash, double focalX, double focalY)
    {
        var focalSalt = FormattableString.Invariant($"{contentHash}|{focalX:0.####}|{focalY:0.####}");
        var hash8 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(focalSalt)))[..8];
        return System.IO.Path.Combine(_directory, $"{Sanitize(wallpaperId)}_{variantKey}_{width}x{height}_{hash8}.png");
    }

    /// <summary>
    /// Returns true and refreshes the LRU stamp when the render already exists. The stamp is the write time (access times are
    /// unreliable on NTFS/relatime); it is only bumped when older than an hour so a hot file is not rewritten constantly.
    /// </summary>
    public static bool TryUse(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromHours(1))
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Access-time bookkeeping is best effort; a read-only file is still a valid cache hit.
        }

        return true;
    }

    /// <summary>Atomically publishes a finished render.</summary>
    public void Commit(string temporaryPath, string finalPath)
    {
        File.Move(temporaryPath, finalPath, overwrite: true);
        Evict();
    }

    public string NewTemporaryPath()
    {
        System.IO.Directory.CreateDirectory(_directory);
        return System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
    }

    /// <summary>LRU eviction above the size cap. Never removes a file that is currently applied to any monitor (FR-APPLY-3).</summary>
    public void Evict()
    {
        lock (_gate)
        {
            if (!System.IO.Directory.Exists(_directory))
            {
                return;
            }

            var files = new DirectoryInfo(_directory).GetFiles("*.png");
            var total = files.Sum(f => f.Length);
            if (total <= _maxBytes)
            {
                return;
            }

            var protectedPaths = _appliedPaths();
            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                if (total <= _maxBytes)
                {
                    break;
                }

                if (protectedPaths.Contains(file.FullName))
                {
                    continue;
                }

                try
                {
                    var length = file.Length;
                    file.Delete();
                    total -= length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (_logger is not null)
                    {
                        LogEvictionFailed(_logger, ex);
                    }
                }
            }
        }
    }

    public long TotalBytes() =>
        System.IO.Directory.Exists(_directory) ? new DirectoryInfo(_directory).GetFiles("*.png").Sum(f => f.Length) : 0;

    private static string Sanitize(string id)
    {
        var builder = new StringBuilder(id.Length);
        foreach (var c in id)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' ? c : '_');
        }

        return builder.ToString();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not evict a render cache file")]
    private static partial void LogEvictionFailed(ILogger logger, Exception ex);
}
