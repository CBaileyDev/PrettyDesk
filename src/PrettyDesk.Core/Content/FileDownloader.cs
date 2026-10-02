using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace PrettyDesk.Core.Content;

public sealed class HashMismatchException(string path, string expected, string actual)
    : IOException($"SHA-256 mismatch for '{Path.GetFileName(path)}' (expected {expected}, got {actual}).");

/// <summary>
/// Downloads one file with HTTP range resume, SHA-256 verification, write-to-temp and atomic rename (FR-CON-3).
/// A hash mismatch discards the partial file and retries from scratch; after <see cref="MaxAttempts"/> it fails.
/// </summary>
public sealed partial class FileDownloader
{
    public const int MaxAttempts = 3;
    public const long MaxDownloadBytes = 8 * 1024 * 1024;
    public static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly ILogger<FileDownloader> _logger;
    private readonly TimeProvider _time;

    public FileDownloader(HttpClient http, TimeProvider time, ILogger<FileDownloader> logger)
    {
        _http = http;
        _time = time;
        _logger = logger;
    }

    /// <summary>Delay before a retry (exponential: 1×, 2×, 4×). Zero in tests.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <param name="expectedBytes">The catalog's declared size; when positive, a response that exceeds it is aborted instead of filling the disk before the hash check.</param>
    public async Task DownloadAsync(Uri url, string destinationPath, string expectedSha256, IProgress<long>? progress, CancellationToken cancellationToken, long expectedBytes = 0)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Only https URLs may be downloaded.", nameof(url));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(expectedBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(expectedBytes, MaxDownloadBytes);
        var maxBytes = expectedBytes > 0 ? expectedBytes : MaxDownloadBytes;

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var partial = destinationPath + ".part";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TransferTimeout, _time);
                using var transfer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                await FetchAsync(url, partial, progress, maxBytes, transfer.Token);
                var actual = await HashFileAsync(partial, cancellationToken);
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(partial);
                    throw new HashMismatchException(destinationPath, expectedSha256, actual);
                }

                File.Move(partial, destinationPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested &&
                ex is IOException or HttpRequestException or OperationCanceledException)
            {
                var fileName = Path.GetFileName(destinationPath);
                var reason = ex.GetType().Name;
                LogRetry(fileName, attempt, reason);
                if (RetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(TimeSpan.FromTicks(RetryDelay.Ticks << (attempt - 1)), _time, cancellationToken);
                }
            }
        }
    }

    private async Task FetchAsync(Uri url, string partial, IProgress<long>? progress, long maxBytes, CancellationToken ct)
    {
        long existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The partial file is stale or already complete; start over so the hash check decides.
            File.Delete(partial);
            existing = 0;
            using var retry = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            retry.EnsureSuccessStatusCode();
            await WriteAsync(retry, partial, append: false, progress, maxBytes, ct);
            return;
        }

        response.EnsureSuccessStatusCode();

        // 206 → server honoured the range, append. 200 → server ignored it, restart the file.
        var append = response.StatusCode == HttpStatusCode.PartialContent && existing > 0;
        await WriteAsync(response, partial, append, progress, maxBytes, ct);
    }

    private static async Task WriteAsync(HttpResponseMessage response, string partial, bool append, IProgress<long>? progress, long maxBytes, CancellationToken ct)
    {
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        var total = append ? target.Length : 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (maxBytes > 0 && total > maxBytes)
            {
                // Larger than the signed catalog says it can be: a misbehaving or compromised host. Stop before the disk fills.
                await target.DisposeAsync();
                File.Delete(partial);
                throw new InvalidDataException($"The download is larger than the {maxBytes} bytes the catalog declares.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            progress?.Report(read);
        }
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Download of {File} failed on attempt {Attempt} ({Reason}); retrying")]
    private partial void LogRetry(string file, int attempt, string reason);
}
