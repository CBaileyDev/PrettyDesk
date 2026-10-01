using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace PrettyDesk.Core.Tests.Content;

public sealed class FileDownloaderTests : IDisposable
{
    private static readonly Uri Url = new("https://content.example.com/v1/packs/p/v1/a.jpg");
    private readonly TempDir _dir = new();
    private readonly byte[] _payload = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 31 % 251)).ToArray();

    public void Dispose() => _dir.Dispose();

    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static FileDownloader Create(FakeHttpHandler handler) =>
        new(new HttpClient(handler), new FakeTimeProvider(), NullLogger<FileDownloader>.Instance) { RetryDelay = TimeSpan.Zero };

    private FakeHttpHandler Server(Func<int, byte[]>? body = null, bool supportRange = true)
    {
        var calls = 0;
        return new FakeHttpHandler(req =>
        {
            var n = Interlocked.Increment(ref calls);
            var data = body?.Invoke(n) ?? _payload;
            if (supportRange && req.Headers.Range?.Ranges.FirstOrDefault()?.From is { } from)
            {
                if (from >= data.Length)
                {
                    return FakeHttpHandler.Status(HttpStatusCode.RequestedRangeNotSatisfiable);
                }

                var slice = data[(int)from..];
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(slice) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, data.Length - 1, data.Length);
                return response;
            }

            return FakeHttpHandler.Ok(data);
        });
    }

    [Fact]
    public async Task Downloads_verifies_and_atomically_publishes_the_file()
    {
        var dest = _dir.File("out/a.jpg");
        var progress = new Progress();

        await Create(Server()).DownloadAsync(Url, dest, Hash(_payload), progress, TestContext.Current.CancellationToken);

        (await File.ReadAllBytesAsync(dest, TestContext.Current.CancellationToken)).ShouldBe(_payload);
        File.Exists(dest + ".part").ShouldBeFalse();
        progress.Total.ShouldBe(_payload.Length);
    }

    [Fact]
    public async Task Resumes_from_a_partial_file_using_a_range_request()
    {
        var dest = _dir.File("a.jpg");
        await File.WriteAllBytesAsync(dest + ".part", _payload[..100_000], TestContext.Current.CancellationToken);
        var handler = Server();

        await Create(handler).DownloadAsync(Url, dest, Hash(_payload), null, TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem().Headers.Range!.Ranges.Single().From.ShouldBe(100_000);
        (await File.ReadAllBytesAsync(dest, TestContext.Current.CancellationToken)).ShouldBe(_payload);
    }

    [Fact]
    public async Task Server_ignoring_range_restarts_the_file_instead_of_corrupting_it()
    {
        var dest = _dir.File("a.jpg");
        await File.WriteAllBytesAsync(dest + ".part", _payload[..50_000], TestContext.Current.CancellationToken);

        await Create(Server(supportRange: false)).DownloadAsync(Url, dest, Hash(_payload), null, TestContext.Current.CancellationToken);

        (await File.ReadAllBytesAsync(dest, TestContext.Current.CancellationToken)).ShouldBe(_payload);
    }

    [Fact]
    public async Task Stale_oversized_partial_is_discarded_on_416()
    {
        var dest = _dir.File("a.jpg");
        await File.WriteAllBytesAsync(dest + ".part", new byte[_payload.Length + 10], TestContext.Current.CancellationToken);

        await Create(Server()).DownloadAsync(Url, dest, Hash(_payload), null, TestContext.Current.CancellationToken);

        (await File.ReadAllBytesAsync(dest, TestContext.Current.CancellationToken)).ShouldBe(_payload);
    }

    [Fact]
    public async Task Hash_mismatch_retries_then_succeeds_when_the_server_recovers()
    {
        var dest = _dir.File("a.jpg");
        var handler = Server(n => n == 1 ? new byte[_payload.Length] : _payload);

        await Create(handler).DownloadAsync(Url, dest, Hash(_payload), null, TestContext.Current.CancellationToken);

        handler.Requests.Count.ShouldBe(2);
        (await File.ReadAllBytesAsync(dest, TestContext.Current.CancellationToken)).ShouldBe(_payload);
    }

    [Fact]
    public async Task Persistent_hash_mismatch_fails_after_three_attempts_and_publishes_nothing()
    {
        var dest = _dir.File("a.jpg");
        var handler = Server(_ => new byte[_payload.Length]);

        await Should.ThrowAsync<HashMismatchException>(() => Create(handler).DownloadAsync(Url, dest, Hash(_payload), null, TestContext.Current.CancellationToken));

        handler.Requests.Count.ShouldBe(FileDownloader.MaxAttempts);
        File.Exists(dest).ShouldBeFalse();
        File.Exists(dest + ".part").ShouldBeFalse();
    }

    [Fact]
    public async Task Existing_good_file_is_not_replaced_by_a_failed_download()
    {
        var dest = _dir.File("a.jpg");
        await File.WriteAllBytesAsync(dest, [1, 2, 3], TestContext.Current.CancellationToken);

        await Should.ThrowAsync<HashMismatchException>(() => Create(Server(_ => [9, 9])).DownloadAsync(Url, dest, Hash(_payload), null, TestContext.Current.CancellationToken));

        (await File.ReadAllBytesAsync(dest, TestContext.Current.CancellationToken)).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Http_errors_are_retried_and_surface_when_persistent()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable));

        await Should.ThrowAsync<HttpRequestException>(() => Create(handler).DownloadAsync(Url, _dir.File("a.jpg"), "00", null, TestContext.Current.CancellationToken));

        handler.Requests.Count.ShouldBe(FileDownloader.MaxAttempts);
    }

    [Fact]
    public async Task Plain_http_is_refused()
    {
        await Should.ThrowAsync<ArgumentException>(() => Create(Server()).DownloadAsync(new Uri("http://content.example.com/a.jpg"), _dir.File("a.jpg"), "00", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancellation_is_not_retried()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = Server();

        await Should.ThrowAsync<OperationCanceledException>(() => Create(handler).DownloadAsync(Url, _dir.File("a.jpg"), Hash(_payload), null, cts.Token));

        handler.Requests.Count.ShouldBeLessThanOrEqualTo(1);
    }

    private sealed class Progress : IProgress<long>
    {
        public long Total { get; private set; }

        public void Report(long value) => Total += value;
    }
}

public sealed class PackStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly PackStore _store;

    public PackStoreTests() => _store = new PackStore(_dir.File("packs"));

    public void Dispose() => _dir.Dispose();

    private void Put(string pack, int version, string file, int kb, DateTime? access = null)
    {
        var directory = _store.VersionDirectory(pack, version);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, file), new byte[kb * 1024]);
        if (access is { } a)
        {
            var marker = Path.Combine(Path.GetDirectoryName(directory)!, ".used");
            File.WriteAllBytes(marker, []);
            File.SetLastWriteTimeUtc(marker, a);
        }
    }

    [Fact]
    public void Layout_follows_pack_and_version_directories()
    {
        _store.FilePath("game.cs2", 3, "packs/game.cs2/v3/cs2.hero-01_16x9.jpg").ShouldEndWith(Path.Combine("game.cs2", "v3", "cs2.hero-01_16x9.jpg"));
    }

    [Fact]
    public void Usage_and_pack_listing()
    {
        Put("a", 1, "x.jpg", 10);
        Put("b", 1, "y.jpg", 20);

        _store.UsageBytes().ShouldBe(30 * 1024);
        _store.InstalledPackIds().ShouldBe(["a", "b"], ignoreOrder: true);
    }

    [Fact]
    public void Versions_are_listed_newest_first()
    {
        Put("a", 1, "x.jpg", 1);
        Put("a", 3, "x.jpg", 1);
        Put("a", 2, "x.jpg", 1);

        _store.Versions("a").Select(v => v.Version).ShouldBe([3, 2, 1]);
    }

    [Fact]
    public void Old_versions_are_removed_after_a_swap()
    {
        Put("a", 1, "x.jpg", 1);
        Put("a", 2, "x.jpg", 1);

        _store.DeleteOldVersions("a", keepVersion: 2);

        _store.Versions("a").Select(v => v.Version).ShouldBe([2]);
    }

    [Fact]
    public void Eviction_removes_least_recently_used_packs_and_skips_protected_ones()
    {
        Put("old", 1, "x.jpg", 400, DateTime.UtcNow.AddDays(-10));
        Put("protected", 1, "x.jpg", 400, DateTime.UtcNow.AddDays(-20));
        Put("fresh", 1, "x.jpg", 400, DateTime.UtcNow.AddDays(-1));

        var freed = _store.EvictToCap(900 * 1024, new HashSet<string> { "protected" });

        freed.ShouldBe(400 * 1024);
        _store.InstalledPackIds().ShouldBe(["protected", "fresh"], ignoreOrder: true);
    }

    [Fact]
    public void Touch_protects_a_pack_from_eviction()
    {
        Put("a", 1, "x.jpg", 400, DateTime.UtcNow.AddDays(-10));
        Put("b", 1, "x.jpg", 400, DateTime.UtcNow.AddDays(-5));

        _store.Touch("a");
        _store.EvictToCap(500 * 1024, new HashSet<string>());

        _store.InstalledPackIds().ShouldBe(["a"]);
    }

    [Fact]
    public void Clear_all_frees_everything_but_kept_packs()
    {
        Put("a", 1, "x.jpg", 10);
        Put("b", 1, "x.jpg", 10);

        _store.ClearAll(new HashSet<string> { "b" }).ShouldBe(10 * 1024);

        _store.InstalledPackIds().ShouldBe(["b"]);
    }

    [Fact]
    public void Loose_files_are_found_across_packs_for_removed_wallpapers()
    {
        Put("a", 1, "gone.hero_16x9.jpg", 1);

        _store.FindLooseFile("gone.hero_16x9.jpg").ShouldNotBeNull();
        _store.FindLooseFilesByPrefix("gone.hero_").Count().ShouldBe(1);
        _store.FindLooseFile("nope.jpg").ShouldBeNull();
    }

    [Fact]
    public void Stale_partial_downloads_are_cleaned_up_but_recent_ones_are_kept()
    {
        Put("a", 1, "x.jpg", 1);
        var directory = _store.VersionDirectory("a", 1);
        var stale = Path.Combine(directory, "old.jpg.part");
        var recent = Path.Combine(directory, "new.jpg.part");
        File.WriteAllBytes(stale, [1]);
        File.WriteAllBytes(recent, [1]);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-3));

        _store.CleanStalePartials(TimeProvider.System);

        File.Exists(stale).ShouldBeFalse();
        File.Exists(recent).ShouldBeTrue();
    }

    [Fact]
    public void Empty_store_is_not_an_error()
    {
        _store.UsageBytes().ShouldBe(0);
        _store.InstalledPackIds().ShouldBeEmpty();
        _store.ClearAll().ShouldBe(0);
        _store.EvictToCap(0, new HashSet<string>()).ShouldBe(0);
    }
}

public sealed class UserImageStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly UserImageStore _store;

    public UserImageStoreTests() => _store = new UserImageStore(_dir.File("user"));

    public void Dispose() => _dir.Dispose();

    private string Source(string name, int w, int h, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        var path = _dir.File(name);
        ImageFactory.Write(path, w, h, format);
        return path;
    }

    /// <summary>Skia cannot encode BMP, so write a minimal 24-bit uncompressed BMP by hand.</summary>
    private static void WriteBmp(string path, int width, int height)
    {
        var rowSize = ((width * 3) + 3) / 4 * 4;
        var pixelBytes = rowSize * height;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(54 + pixelBytes);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        writer.Write(new byte[pixelBytes]);
    }

    [Theory]
    [InlineData("a.png", SKEncodedImageFormat.Png)]
    [InlineData("a.jpg", SKEncodedImageFormat.Jpeg)]
    [InlineData("a.jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("a.webp", SKEncodedImageFormat.Webp)]
    public void Imports_supported_formats(string name, SKEncodedImageFormat format)
    {
        var result = _store.Import(Source(name, 1920, 1080, format));

        result.Ok.ShouldBeTrue(result.Message);
        result.Image!.Id.ShouldStartWith("user:");
        File.Exists(result.Image.Path).ShouldBeTrue();
        (result.Image.Width, result.Image.Height).ShouldBe((1920, 1080));
    }

    [Fact]
    public void Bmp_is_accepted_by_extension_when_skia_can_decode_it()
    {
        var path = _dir.File("a.bmp");
        WriteBmp(path, 1400, 800);

        _store.Import(path).Ok.ShouldBeTrue();
    }

    [Fact]
    public void Images_under_1280_px_on_the_long_edge_are_rejected_with_a_human_message()
    {
        var result = _store.Import(Source("small.png", 1000, 600));

        result.Failure.ShouldBe(ImportFailure.TooSmall);
        result.Message.ShouldNotBeNull().ShouldContain("1280");
    }

    [Fact]
    public void Exactly_1280_on_the_long_edge_is_allowed() =>
        _store.Import(Source("edge.png", 1280, 720)).Ok.ShouldBeTrue();

    [Fact]
    public void Portrait_images_are_judged_by_their_long_edge() =>
        _store.Import(Source("portrait.png", 800, 1400)).Ok.ShouldBeTrue();

    [Fact]
    public void Unsupported_extension_and_garbage_content_and_missing_files_are_distinct_failures()
    {
        var txt = _dir.File("a.gif");
        File.WriteAllText(txt, "x");
        var fake = _dir.File("fake.png");
        File.WriteAllText(fake, "not really a png");

        _store.Import(txt).Failure.ShouldBe(ImportFailure.UnsupportedFormat);
        _store.Import(fake).Failure.ShouldBe(ImportFailure.Unreadable);
        _store.Import(_dir.File("missing.png")).Failure.ShouldBe(ImportFailure.NotFound);
    }

    [Fact]
    public void Importing_the_same_file_twice_yields_one_copy()
    {
        var source = Source("dup.png", 1600, 900);

        var first = _store.Import(source).Image!;
        var second = _store.Import(source).Image!;

        second.Id.ShouldBe(first.Id);
        Directory.GetFiles(_dir.File("user")).Length.ShouldBe(1);
    }

    [Fact]
    public void Imported_copy_is_independent_of_the_original()
    {
        var source = Source("orig.png", 1600, 900);
        var image = _store.Import(source).Image!;

        File.Delete(source);

        _store.TryGet(image.Id).ShouldNotBeNull();
    }

    [Fact]
    public void Ids_cannot_escape_the_user_folder()
    {
        File.WriteAllText(_dir.File("secret.png"), "x");

        _store.TryGet("user:../secret.png").ShouldBeNull();
        _store.TryGet("user:").ShouldBeNull();
        _store.TryGet("not-a-user-id").ShouldBeNull();
    }

    [Fact]
    public void List_and_remove()
    {
        var a = _store.Import(Source("a.png", 1600, 900)).Image!;
        var b = _store.Import(Source("b.png", 1700, 900)).Image!;

        _store.List().Select(i => i.Id).ShouldBe([a.Id, b.Id], ignoreOrder: true);
        _store.Remove(a.Id).ShouldBeTrue();
        _store.Remove(a.Id).ShouldBeFalse();
        _store.List().ShouldHaveSingleItem().Id.ShouldBe(b.Id);
    }

    [Fact]
    public void Resolution_warning_when_the_image_is_smaller_than_a_monitor()
    {
        var image = new UserImage("user:x.png", "/x.png", 1920, 1080);

        UserImageStore.ResolutionWarning(image, [new MonitorInfo("m", 0, 0, 1920, 1080, true)]).ShouldBeNull();
        UserImageStore.ResolutionWarning(image, [new MonitorInfo("m", 0, 0, 3840, 2160, true)]).ShouldNotBeNull().ShouldContain("3840×2160");
    }
}
