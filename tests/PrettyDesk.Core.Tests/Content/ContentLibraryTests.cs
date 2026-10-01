using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Content;

public sealed class ContentLibraryTests : IDisposable
{
    private const string Base = "https://content.example.com/v1/";
    private static readonly MonitorInfo Hd = new("hd", 0, 0, 1920, 1080, true);
    private static readonly MonitorInfo Ultra = new("uw", 0, 0, 3440, 1440, false);

    private readonly TempDir _dir = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, byte[]> _server = [];
    private readonly FakeCatalog _catalog;
    private readonly PackStore _packs;
    private ContentLibrary? _library;

    public ContentLibraryTests()
    {
        _packs = new PackStore(_dir.File("packs"));
        _catalog = new FakeCatalog(MakeCatalog(version: 1));
    }

    public void Dispose()
    {
        _library?.Dispose();
        _dir.Dispose();
    }

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private FileRef Publish(string path, int w, int h, int seed)
    {
        var bytes = Enumerable.Range(0, 5000).Select(i => (byte)((i * seed) % 253)).ToArray();
        _server[path] = bytes;
        return new FileRef { Path = path, W = w, H = h, Bytes = bytes.Length, Sha256 = Sha(bytes) };
    }

    private CatalogDocument MakeCatalog(int version, params string[] wallpaperIds)
    {
        var ids = wallpaperIds.Length == 0 ? ["cs2.hero-01", "cs2.min-01"] : wallpaperIds;
        var wallpapers = ids.Select((id, i) => new WallpaperEntry
        {
            Id = id,
            Title = id,
            Tone = Tones.Dark,
            Focal = new FocalPoint(0.6, 0.4),
            Thumb = Publish($"packs/game.cs2/v{version}/{id}_thumb.jpg", 640, 360, 7 + i + version),
            Variants = new Dictionary<string, FileRef>
            {
                ["16x9"] = Publish($"packs/game.cs2/v{version}/{id}_16x9.jpg", 3840, 2160, 11 + i + version),
                ["21x9"] = Publish($"packs/game.cs2/v{version}/{id}_21x9.jpg", 5120, 2160, 13 + i + version),
            },
        }).ToList();
        return new CatalogDocument
        {
            CatalogVersion = $"2026.10.01.{version}",
            ContentBaseUrl = Base,
            Packs = [new PackEntry { Id = "game.cs2", Version = version, Title = "CS2", Wallpapers = wallpapers }],
            Collections = [],
        };
    }

    private ContentLibrary Create(HttpMessageHandler? handler = null, string? bundled = null, Func<IReadOnlySet<string>>? protectedPacks = null, long cap = long.MaxValue)
    {
        handler ??= new FakeHttpHandler(Serve);
        var downloader = new FileDownloader(new HttpClient(handler), _time, NullLogger<FileDownloader>.Instance) { RetryDelay = TimeSpan.Zero };
        _library?.Dispose();
        _library = new ContentLibrary(
            _catalog, _packs, new UserImageStore(_dir.File("user")), downloader,
            new ContentLibraryOptions(bundled),
            protectedPacks ?? (() => new HashSet<string>()), () => cap, _time, NullLogger<ContentLibrary>.Instance);
        return _library;
    }

    private HttpResponseMessage Serve(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/')["v1/".Length..];
        return _server.TryGetValue(path, out var bytes) ? FakeHttpHandler.Ok(bytes) : FakeHttpHandler.Status(HttpStatusCode.NotFound);
    }

    private string[] LocalFiles(int version = 1) =>
        Directory.Exists(_packs.VersionDirectory("game.cs2", version)) ? Directory.GetFiles(_packs.VersionDirectory("game.cs2", version)).Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.Ordinal).ToArray() : [];

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var library = Create();
        library.Dispose();

        Should.NotThrow(() => library.Dispose());
    }

    [Fact]
    public void Nothing_is_available_before_download()
    {
        var library = Create();

        library.TryGetAsset("cs2.hero-01").ShouldBeNull();
        library.GetWallpaperIds("game.cs2").ShouldBe(["cs2.hero-01", "cs2.min-01"]);
        library.GetPackState("game.cs2").State.ShouldBe(PackStateKind.NotDownloaded);
    }

    [Fact]
    public async Task Only_the_variants_the_attached_monitors_need_are_downloaded_plus_thumbnails()
    {
        var library = Create();

        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);

        LocalFiles().ShouldBe(["cs2.hero-01_16x9.jpg", "cs2.hero-01_thumb.jpg", "cs2.min-01_16x9.jpg", "cs2.min-01_thumb.jpg"]);
        var asset = library.TryGetAsset("cs2.hero-01").ShouldNotBeNull();
        asset.Variants.Keys.ShouldBe(["16x9"]);
        asset.Focal.X.ShouldBe(0.6);
        library.GetPackState("game.cs2").State.ShouldBe(PackStateKind.Ready);
    }

    [Fact]
    public async Task Plugging_in_an_ultrawide_later_fetches_only_the_missing_variant()
    {
        var library = Create();
        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);
        var handler = (FakeHttpHandler)new FakeHttpHandler(Serve);
        library = Create(handler);

        await library.EnsurePackAsync("game.cs2", [Hd, Ultra], TestContext.Current.CancellationToken);

        handler.Requests.Select(r => Path.GetFileName(r.RequestUri!.AbsolutePath)).OrderBy(n => n).ToArray()
            .ShouldBe(["cs2.hero-01_21x9.jpg", "cs2.min-01_21x9.jpg"]);
        library.TryGetAsset("cs2.hero-01")!.Variants.Keys.ShouldBe(["16x9", "21x9"], ignoreOrder: true);
    }

    [Fact]
    public async Task Pack_changed_is_raised_as_wallpapers_land_and_at_completion()
    {
        var library = Create();
        var changes = 0;
        library.PackChanged += _ => Interlocked.Increment(ref changes);

        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);

        changes.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Progress_reports_downloading_then_ready()
    {
        var library = Create();
        var states = new List<PackStateKind>();
        library.ProgressChanged += p => states.Add(p.State);

        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);

        states.First().ShouldBe(PackStateKind.Downloading);
        states.Last().ShouldBe(PackStateKind.Ready);
    }

    [Fact]
    public async Task Concurrent_requests_for_one_pack_share_a_single_download()
    {
        var handler = new FakeHttpHandler(Serve);
        var library = Create(handler);

        await Task.WhenAll(
            library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken),
            library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken));

        handler.Requests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task At_most_two_downloads_run_at_once()
    {
        var handler = new GatedHandler(Serve);
        var library = Create(handler);
        var run = library.EnsurePackAsync("game.cs2", [Hd, Ultra], TestContext.Current.CancellationToken);

        SpinWait.SpinUntil(() => handler.Active >= 2, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        handler.MaxActive.ShouldBe(2);

        handler.Open();
        await run;
        handler.MaxActive.ShouldBe(2);
    }

    [Fact]
    public async Task Failed_download_reports_the_failure_keeps_old_content_and_backs_off()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.InternalServerError));
        var library = Create(handler);
        Exception? failure = null;
        library.DownloadFailed += (_, ex) => failure = ex;

        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);

        failure.ShouldNotBeNull();
        library.GetPackState("game.cs2").State.ShouldBe(PackStateKind.Failed);
        library.TryGetAsset("cs2.hero-01").ShouldBeNull();

        var before = handler.Requests.Count;
        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);
        handler.Requests.Count.ShouldBe(before);

        _time.Advance(TimeSpan.FromMinutes(3));
        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);
        handler.Requests.Count.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task Tampered_server_content_is_never_published()
    {
        _server["packs/game.cs2/v1/cs2.hero-01_16x9.jpg"] = [1, 2, 3];
        var library = Create();
        Exception? failure = null;
        library.DownloadFailed += (_, ex) => failure = ex;

        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);

        failure.ShouldBeOfType<HashMismatchException>();
        File.Exists(_packs.FilePath("game.cs2", 1, "x/cs2.hero-01_16x9.jpg")).ShouldBeFalse();
    }

    [Fact]
    public async Task Bundled_starter_set_is_used_without_any_download()
    {
        var bundled = _dir.File("bundled");
        Directory.CreateDirectory(bundled);
        await File.WriteAllBytesAsync(Path.Combine(bundled, "cs2.hero-01_16x9.jpg"), _server["packs/game.cs2/v1/cs2.hero-01_16x9.jpg"], TestContext.Current.CancellationToken);
        var handler = new FakeHttpHandler(Serve);
        var library = Create(handler, bundled);

        library.TryGetAsset("cs2.hero-01").ShouldNotBeNull().Variants["16x9"].Path.ShouldStartWith(bundled);

        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);
        handler.Requests.Select(r => Path.GetFileName(r.RequestUri!.AbsolutePath)).ShouldNotContain("cs2.hero-01_16x9.jpg");
    }

    [Fact]
    public async Task Updating_a_pack_downloads_the_new_version_keeps_old_until_swap_then_drops_superseded_files()
    {
        var library = Create();
        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);
        var v1Path = library.TryGetAsset("cs2.hero-01")!.Variants["16x9"].Path;

        _catalog.Current = MakeCatalog(version: 2, "cs2.hero-01");
        _catalog.RaiseChanged();

        // Before the new files arrive, the old version still serves the wallpaper.
        library.TryGetAsset("cs2.hero-01")!.Variants["16x9"].Path.ShouldBe(v1Path);

        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);

        var v2Path = library.TryGetAsset("cs2.hero-01")!.Variants["16x9"].Path;
        v2Path.ShouldContain(Path.Combine("game.cs2", "v2"));
        File.Exists(v1Path).ShouldBeFalse();
        LocalFiles(1).ShouldBe(["cs2.min-01_16x9.jpg", "cs2.min-01_thumb.jpg"]);
    }

    [Fact]
    public async Task Wallpaper_removed_from_the_catalog_stays_usable_from_its_old_files()
    {
        var library = Create();
        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);

        _catalog.Current = MakeCatalog(version: 2, "cs2.hero-01");
        _catalog.RaiseChanged();

        var removed = library.TryGetAsset("cs2.min-01").ShouldNotBeNull();
        removed.Variants.ContainsKey("16x9").ShouldBeTrue();
    }

    [Fact]
    public async Task Different_content_yields_a_different_content_hash()
    {
        var library = Create();
        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);
        var a = library.TryGetAsset("cs2.hero-01")!.ContentHash;
        var b = library.TryGetAsset("cs2.min-01")!.ContentHash;

        a.ShouldNotBe(b);
        library.TryGetAsset("cs2.hero-01")!.ContentHash.ShouldBe(a);
    }

    [Fact]
    public async Task Storage_cap_evicts_other_packs_but_never_protected_ones()
    {
        var other = _packs.VersionDirectory("old.pack", 1);
        Directory.CreateDirectory(other);
        await File.WriteAllBytesAsync(Path.Combine(other, "big.jpg"), new byte[200_000], TestContext.Current.CancellationToken);
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(other)!, DateTime.UtcNow.AddDays(-30));
        var library = Create(cap: 50_000);

        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);

        _packs.InstalledPackIds().ShouldNotContain("old.pack");
        _packs.InstalledPackIds().ShouldContain("game.cs2");
    }

    [Fact]
    public async Task Protected_packs_survive_eviction()
    {
        var other = _packs.VersionDirectory("keep.pack", 1);
        Directory.CreateDirectory(other);
        await File.WriteAllBytesAsync(Path.Combine(other, "big.jpg"), new byte[200_000], TestContext.Current.CancellationToken);
        var library = Create(protectedPacks: () => new HashSet<string> { "keep.pack" }, cap: 50_000);

        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);

        _packs.InstalledPackIds().ShouldContain("keep.pack");
    }

    [Fact]
    public async Task Thumbnails_are_exposed_for_the_library_grid()
    {
        var library = Create();
        library.GetThumbnailPath("cs2.hero-01").ShouldBeNull();

        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);

        library.GetThumbnailPath("cs2.hero-01").ShouldNotBeNull().ShouldEndWith("cs2.hero-01_thumb.jpg");
    }

    [Fact]
    public async Task A_catalog_without_a_content_url_downloads_nothing_and_does_not_fail()
    {
        _catalog.Current = _catalog.Current with { ContentBaseUrl = string.Empty };
        var handler = new FakeHttpHandler(Serve);
        var library = Create(handler);
        var failed = false;
        library.DownloadFailed += (_, _) => failed = true;

        await library.EnsurePackAsync("game.cs2", [Hd], TestContext.Current.CancellationToken);

        handler.Requests.ShouldBeEmpty();
        failed.ShouldBeFalse();
    }

    [Fact]
    public void Unknown_pack_requests_are_ignored_quietly()
    {
        var library = Create();

        Should.NotThrow(() => library.RequestPack("game.nope", [Hd]));
        library.GetWallpaperIds("game.nope").ShouldBeEmpty();
    }

    [Fact]
    public void User_images_resolve_as_assets_and_user_pack_lists_them()
    {
        var library = Create();
        var source = _dir.File("mine.png");
        ImageFactory.Write(source, 2000, 1000);
        var image = library.UserImages.Import(source).Image!;

        var asset = library.TryGetAsset(image.Id).ShouldNotBeNull();

        asset.IsUserImage.ShouldBeTrue();
        asset.Variants.Keys.ShouldBe(["user"]);
        library.GetWallpaperIds("user").ShouldBe([image.Id]);
        library.TryGetAsset("user:../../etc/passwd").ShouldBeNull();
    }

    private sealed class GatedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _max;

        public int Active => Volatile.Read(ref _active);

        public int MaxActive => Volatile.Read(ref _max);

        public void Open() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _active);
            int seen;
            while (now > (seen = Volatile.Read(ref _max)))
            {
                Interlocked.CompareExchange(ref _max, now, seen);
            }

            try
            {
                await _gate.Task.WaitAsync(cancellationToken);
                return respond(request);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
