using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using PrettyDesk.Core.Abstractions;

namespace PrettyDesk.Core.Catalog;

public sealed record CatalogServiceOptions(string BundledCatalogPath, string CacheDirectory, string ContentBaseUrl, Version AppVersion);

/// <summary>
/// Loads the bundled snapshot / last good catalog, and refreshes from <c>ContentBaseUrl</c> on startup and every 24 h with an
/// ETag (FR-CON-2). A catalog is used only when its ECDSA signature verifies, its schema and <c>minAppVersion</c> are
/// supported, it is structurally sound and it is not older than what is already loaded. Every failure is non-fatal and
/// quiet (NFR-7): the last good catalog stays in place.
/// </summary>
public sealed partial class CatalogService : ICatalogProvider, IDisposable
{
    public const int MaxCatalogBytes = 8 * 1024 * 1024;
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    private readonly CatalogServiceOptions _options;
    private readonly HttpClient _http;
    private readonly CatalogSignatureVerifier _verifier;
    private readonly TimeProvider _time;
    private readonly ILogger<CatalogService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private ITimer? _timer;

    public CatalogService(CatalogServiceOptions options, HttpClient http, CatalogSignatureVerifier verifier, TimeProvider time, ILogger<CatalogService> logger)
    {
        _options = options;
        _http = http;
        _verifier = verifier;
        _time = time;
        _logger = logger;
        Current = CatalogDocument.Empty;
    }

    public CatalogDocument Current { get; private set; }

    public event Action? Changed;

    private string CatalogPath => Path.Combine(_options.CacheDirectory, "catalog.json");

    private string SignaturePath => CatalogPath + ".sig";

    private string EtagPath => CatalogPath + ".etag";

    /// <summary>Picks the best of the cached (re-verified) catalog and the bundled snapshot. Never throws.</summary>
    public void LoadInitial()
    {
        var best = CatalogDocument.Empty;

        var bundled = TryLoadFile(_options.BundledCatalogPath, signatureRequired: false);
        if (bundled is not null)
        {
            best = bundled;
        }

        var cached = TryLoadFile(CatalogPath, signatureRequired: true);
        if (cached is not null && CatalogVersion.Compare(cached.CatalogVersion, best.CatalogVersion) > 0)
        {
            best = cached;
        }

        Current = best;
        LogLoaded(best.CatalogVersion, best.Games.Count);
    }

    public void StartPeriodicRefresh()
    {
        _timer ??= _time.CreateTimer(_ => _ = RefreshAsync(CancellationToken.None), null, TimeSpan.Zero, RefreshInterval);
    }

    /// <summary>Fetches and applies a newer signed catalog. Returns true when <see cref="Current"/> changed.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        if (!HasValidBaseUrl(_options.ContentBaseUrl))
        {
            LogNoContentUrl();
            return false;
        }

        if (!_verifier.HasKeys)
        {
            LogNoTrustedKeys();
            return false;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            return await RefreshCoreAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                LogRefreshFailed(ex);
            }

            return false;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>True when a build-time <c>ContentBaseUrl</c> is an absolute https URL ending in '/'.</summary>
    public static bool HasValidBaseUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.EndsWith('/') && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    public void Dispose()
    {
        _timer?.Dispose();
        _refreshGate.Dispose();
    }

    private async Task<bool> RefreshCoreAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.ContentBaseUrl + "catalog.json");
        if (File.Exists(EtagPath) && File.Exists(CatalogPath))
        {
            var etag = await File.ReadAllTextAsync(EtagPath, ct);
            if (EntityTagHeaderValue.TryParse(etag.Trim(), out var tag))
            {
                request.Headers.IfNoneMatch.Add(tag);
            }
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        var catalogBytes = await ReadBoundedAsync(response, ct);

        using var sigResponse = await _http.GetAsync(_options.ContentBaseUrl + "catalog.json.sig", ct);
        sigResponse.EnsureSuccessStatusCode();
        var signature = await sigResponse.Content.ReadAsStringAsync(ct);

        var check = Evaluate(catalogBytes, signature);
        if (!check.Ok)
        {
            LogRejected(check.Rejection, check.Detail ?? string.Empty);
            return false;
        }

        Directory.CreateDirectory(_options.CacheDirectory);
        WriteAtomic(CatalogPath, catalogBytes);
        WriteAtomic(SignaturePath, Encoding.UTF8.GetBytes(signature.Trim()));
        if (response.Headers.ETag is { } newTag)
        {
            WriteAtomic(EtagPath, Encoding.UTF8.GetBytes(newTag.ToString()));
        }

        Current = check.Document!;
        LogUpdated(Current.CatalogVersion);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Signature → parse/validate → rollback check. Order matters: nothing is parsed before it is authenticated.</summary>
    public CatalogCheck Evaluate(byte[] catalogBytes, string signatureBase64)
    {
        if (!_verifier.VerifyBase64(catalogBytes, signatureBase64))
        {
            return new CatalogCheck(null, CatalogRejection.BadSignature, null);
        }

        var parsed = CatalogValidator.ParseAndValidate(catalogBytes, _options.AppVersion);
        if (!parsed.Ok)
        {
            return parsed;
        }

        if (CatalogVersion.Compare(parsed.Document!.CatalogVersion, Current.CatalogVersion) < 0)
        {
            return new CatalogCheck(null, CatalogRejection.OlderThanCurrent, parsed.Document.CatalogVersion);
        }

        return parsed;
    }

    private CatalogDocument? TryLoadFile(string path, bool signatureRequired)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            if (signatureRequired)
            {
                var sigPath = path + ".sig";
                if (!File.Exists(sigPath) || !_verifier.VerifyBase64(bytes, File.ReadAllText(sigPath)))
                {
                    return null;
                }
            }

            var check = CatalogValidator.ParseAndValidate(bytes, _options.AppVersion);
            return check.Ok ? check.Document : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxCatalogBytes)
        {
            throw new HttpRequestException("catalog too large");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxCatalogBytes)
            {
                throw new HttpRequestException("catalog too large");
            }
        }

        return buffer.ToArray();
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog {Version} loaded ({Games} games)")]
    private partial void LogLoaded(string version, int games);

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog updated to {Version}")]
    private partial void LogUpdated(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote catalog rejected: {Reason} ({Detail}); keeping the last good catalog")]
    private partial void LogRejected(CatalogRejection reason, string detail);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Catalog refresh failed; will try again later")]
    private partial void LogRefreshFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "No content URL is configured for this build; running on the bundled catalog")]
    private partial void LogNoContentUrl();

    [LoggerMessage(Level = LogLevel.Information, Message = "No trusted catalog keys are compiled in; remote catalog updates are disabled")]
    private partial void LogNoTrustedKeys();
}
