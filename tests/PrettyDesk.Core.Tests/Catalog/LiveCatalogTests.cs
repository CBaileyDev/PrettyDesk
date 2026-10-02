using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Catalog;

public sealed class LiveCatalogTests
{
    [Fact]
    public async Task Real_https_feed_uses_shipped_key_and_preserves_cache_when_content_is_tampered_or_unsigned()
    {
        var url = Environment.GetEnvironmentVariable("PRETTYDESK_TEST_CATALOG_URL");
        var certificateHash = Environment.GetEnvironmentVariable("PRETTYDESK_TEST_CERT_SHA256");
        Assert.SkipWhen(string.IsNullOrEmpty(url), "Run tools/catalog_acceptance.ps1 to start the isolated HTTPS fixture.");
        certificateHash.ShouldNotBeNullOrEmpty();
        using var dir = new TempDir();
        using var handler = new HttpClientHandler
        {
            // This opt-in fixture trusts exactly one test certificate, never all certificates.
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null && Convert.ToHexString(SHA256.HashData(cert.RawData)).Equals(certificateHash, StringComparison.OrdinalIgnoreCase),
        };
        using var http = new HttpClient(handler);
        CatalogService Create(string endpoint) => new(
            new CatalogServiceOptions(dir.File("absent-bundle.json"), dir.File("cache"), url + endpoint + "/", new Version(1, 0, 1)),
            http, TrustedKeys.CreateVerifier(), TimeProvider.System, NullLogger<CatalogService>.Instance);
        using var valid = Create("valid");
        valid.LoadInitial();
        (await valid.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        valid.Current.Packs.Sum(p => p.Wallpapers.Count).ShouldBeGreaterThanOrEqualTo(13);
        var starter = valid.Current.Packs.SelectMany(p => p.Wallpapers).First(w => w.Starter);
        var downloader = new FileDownloader(http, TimeProvider.System, NullLogger<FileDownloader>.Instance) { RetryDelay = TimeSpan.Zero };
        var starterPack = valid.Current.Packs.First(p => p.Wallpapers.Any(w => w.Starter));
        using var content = new ContentLibrary(valid, new PackStore(dir.File("packs")), new UserImageStore(dir.File("user")),
            downloader, new ContentLibraryOptions(null), () => new HashSet<string>(), () => 500_000_000L,
            TimeProvider.System, NullLogger<ContentLibrary>.Instance);
        await content.EnsurePackAsync(starterPack.Id, [new MonitorInfo("fixture", 0, 0, 1920, 1080, true)], TestContext.Current.CancellationToken);
        content.GetPackState(starterPack.Id).State.ShouldBe(PackStateKind.Ready);
        content.TryGetAsset(starter.Id).ShouldNotBeNull().Variants.ShouldContainKey("16x9");
        content.GetThumbnailPath(starter.Id).ShouldNotBeNullOrEmpty();
        foreach (var file in new[] { starter.Variants["16x9"], starter.Thumb.ShouldNotBeNull() })
        {
            var destination = dir.File("downloads/" + Path.GetFileName(file.Path));
            await downloader.DownloadAsync(new Uri(url + "valid/" + file.Path), destination, file.Sha256, null, TestContext.Current.CancellationToken);
            new FileInfo(destination).Length.ShouldBe(file.Bytes);
            File.Exists(destination + ".part").ShouldBeFalse();
        }

        var badDestination = dir.File("downloads/rejected.jpg");
        await Should.ThrowAsync<HashMismatchException>(() => downloader.DownloadAsync(new Uri(url + "bad-asset.jpg"),
            badDestination, starter.Variants["16x9"].Sha256, null, TestContext.Current.CancellationToken));
        File.Exists(badDestination).ShouldBeFalse();
        var version = valid.Current.CatalogVersion;
        foreach (var endpoint in new[] { "tampered", "unsigned" })
        {
            using var rejected = Create(endpoint);
            rejected.LoadInitial();
            rejected.Current.CatalogVersion.ShouldBe(version);
            (await rejected.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
            rejected.Current.CatalogVersion.ShouldBe(version);
        }
    }
}
