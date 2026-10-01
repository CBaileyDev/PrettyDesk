using Microsoft.Extensions.Logging.Abstractions;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Imaging;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace PrettyDesk.Core.Tests.Imaging;

public sealed class WallpaperRendererTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly RenderCache _cache;
    private readonly WallpaperRenderer _renderer;

    public WallpaperRendererTests()
    {
        _cache = new RenderCache(_dir.File("render"), () => new HashSet<string>());
        _renderer = new WallpaperRenderer(_cache, NullLogger<WallpaperRenderer>.Instance);
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _dir.Dispose();
    }

    private WallpaperAsset Asset(string id, int w, int h, FocalPoint? focal = null, (double, double, double, double)? marker = null, string variantKey = "16x9")
    {
        var path = _dir.File($"{id}_{variantKey}.png");
        ImageFactory.Write(path, w, h, marker: marker);
        return new WallpaperAsset(id, "pack", id, Tones.Dark, focal ?? FocalPoint.Center,
            new Dictionary<string, LocalVariant> { [variantKey] = new LocalVariant(variantKey, path, w, h) }, "abcdef0123456789");
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1200)]
    [InlineData(3440, 1440)]
    [InlineData(1080, 1920)]
    [InlineData(2560, 1440)]
    [InlineData(5120, 1440)]
    public async Task Output_is_exactly_the_monitor_pixel_size(int w, int h)
    {
        var asset = Asset("w1", 1600, 900);

        var path = await _renderer.RenderAsync(asset, new MonitorInfo("m", 0, 0, w, h, true), TestContext.Current.CancellationToken);

        using var decoded = ImageFactory.Decode(path);
        decoded.Width.ShouldBe(w);
        decoded.Height.ShouldBe(h);
    }

    [Fact]
    public async Task Output_is_a_png_not_a_jpeg()
    {
        var asset = Asset("w2", 800, 450);

        var path = await _renderer.RenderAsync(asset, new MonitorInfo("m", 0, 0, 640, 360, true), TestContext.Current.CancellationToken);

        path.ShouldEndWith(".png");
        var header = new byte[8];
        await using (var stream = File.OpenRead(path))
        {
            await stream.ReadExactlyAsync(header, TestContext.Current.CancellationToken);
        }

        header.ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }

    [Fact]
    public async Task Focal_point_decides_which_part_survives_a_crop()
    {
        // 16:9 source cropped to 3:2 loses ~16% of width. A marker at 93-99% across survives only if the crop follows the focal point.
        var marker = (0.93, 0.4, 0.06, 0.2);
        var focused = Asset("focus", 1600, 900, new FocalPoint(0.96, 0.5), marker);
        var centered = Asset("center", 1600, 900, FocalPoint.Center, marker);
        var monitor = new MonitorInfo("m", 0, 0, 900, 600, true);

        var focusedPath = await _renderer.RenderAsync(focused, monitor, TestContext.Current.CancellationToken);
        var centeredPath = await _renderer.RenderAsync(centered, monitor, TestContext.Current.CancellationToken);

        RedPixelCount(focusedPath).ShouldBeGreaterThan(1000);
        RedPixelCount(centeredPath).ShouldBe(0);
    }

    [Fact]
    public async Task Same_inputs_hit_the_cache_and_reuse_the_same_file()
    {
        var asset = Asset("cached", 800, 450);
        var monitor = new MonitorInfo("m", 0, 0, 640, 360, true);

        var first = await _renderer.RenderAsync(asset, monitor, TestContext.Current.CancellationToken);
        var stamp = File.GetLastWriteTimeUtc(first);
        var second = await _renderer.RenderAsync(asset, monitor, TestContext.Current.CancellationToken);

        second.ShouldBe(first);
        File.GetLastWriteTimeUtc(second).ShouldBe(stamp);
    }

    [Fact]
    public async Task Different_content_or_focal_or_size_yield_different_paths()
    {
        var a = Asset("same-id", 800, 450);
        var monitor = new MonitorInfo("m", 0, 0, 640, 360, true);
        var pathA = await _renderer.RenderAsync(a, monitor, TestContext.Current.CancellationToken);

        var changedHash = a with { ContentHash = "ffffffff00000000" };
        var changedFocal = a with { Focal = new FocalPoint(0.1, 0.1) };
        var otherMonitor = new MonitorInfo("m", 0, 0, 800, 450, true);

        (await _renderer.RenderAsync(changedHash, monitor, TestContext.Current.CancellationToken)).ShouldNotBe(pathA);
        (await _renderer.RenderAsync(changedFocal, monitor, TestContext.Current.CancellationToken)).ShouldNotBe(pathA);
        (await _renderer.RenderAsync(a, otherMonitor, TestContext.Current.CancellationToken)).ShouldNotBe(pathA);
    }

    [Fact]
    public async Task File_name_follows_the_documented_pattern_and_is_filesystem_safe()
    {
        var asset = Asset("user_abc", 800, 450, variantKey: "user") with { Id = "user:abc.png" };

        var path = await _renderer.RenderAsync(asset, new MonitorInfo("m", 0, 0, 640, 360, true), TestContext.Current.CancellationToken);

        Path.GetFileName(path).ShouldMatch(@"^user_abc\.png_user_640x360_[0-9a-f]{8}\.png$");
    }

    [Fact]
    public async Task Undecodable_source_fails_cleanly_and_leaves_no_temp_files()
    {
        var bad = _dir.File("bad.png");
        await File.WriteAllTextAsync(bad, "not an image", TestContext.Current.CancellationToken);
        var asset = new WallpaperAsset("bad", "p", "bad", Tones.Dark, FocalPoint.Center,
            new Dictionary<string, LocalVariant> { ["16x9"] = new LocalVariant("16x9", bad, 1600, 900) }, "hash");

        await Should.ThrowAsync<InvalidDataException>(() => _renderer.RenderAsync(asset, new MonitorInfo("m", 0, 0, 640, 360, true), TestContext.Current.CancellationToken));

        Directory.GetFiles(_dir.File("render")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Wallpaper_without_any_variant_throws_a_clear_error()
    {
        var asset = new WallpaperAsset("none", "p", "none", Tones.Dark, FocalPoint.Center, new Dictionary<string, LocalVariant>(), "h");

        await Should.ThrowAsync<InvalidOperationException>(() => _renderer.RenderAsync(asset, new MonitorInfo("m", 0, 0, 640, 360, true), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_requests_for_the_same_render_produce_one_file()
    {
        var asset = Asset("concurrent", 1600, 900);
        var monitor = new MonitorInfo("m", 0, 0, 1280, 720, true);

        var paths = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _renderer.RenderAsync(asset, monitor, TestContext.Current.CancellationToken)));

        paths.Distinct().Count().ShouldBe(1);
        Directory.GetFiles(_dir.File("render"), "*.png").Length.ShouldBe(1);
    }

    private static int RedPixelCount(string path)
    {
        using var bitmap = ImageFactory.Decode(path);
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red > 200 && c.Green < 60 && c.Blue < 60)
                {
                    count++;
                }
            }
        }

        return count;
    }
}

public sealed class RenderCacheTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Make(string name, int kb, DateTime lastAccess)
    {
        Directory.CreateDirectory(_dir.File("c"));
        var path = _dir.File("c/" + name);
        File.WriteAllBytes(path, new byte[kb * 1024]);
        File.SetLastWriteTimeUtc(path, lastAccess);
        return path;
    }

    [Fact]
    public void Evicts_least_recently_used_files_above_the_cap()
    {
        var old = Make("old.png", 400, DateTime.UtcNow.AddDays(-3));
        var mid = Make("mid.png", 400, DateTime.UtcNow.AddDays(-2));
        var fresh = Make("fresh.png", 400, DateTime.UtcNow.AddDays(-1));
        var cache = new RenderCache(_dir.File("c"), () => new HashSet<string>(), maxBytes: 900 * 1024);

        cache.Evict();

        File.Exists(old).ShouldBeFalse();
        File.Exists(mid).ShouldBeTrue();
        File.Exists(fresh).ShouldBeTrue();
    }

    [Fact]
    public void Never_evicts_a_file_that_is_currently_applied()
    {
        var applied = Make("applied.png", 400, DateTime.UtcNow.AddDays(-30));
        var other = Make("other.png", 400, DateTime.UtcNow.AddDays(-1));
        var cache = new RenderCache(_dir.File("c"), () => new HashSet<string> { applied }, maxBytes: 500 * 1024);

        cache.Evict();

        File.Exists(applied).ShouldBeTrue();
        File.Exists(other).ShouldBeFalse();
    }

    [Fact]
    public void Does_nothing_below_the_cap()
    {
        var a = Make("a.png", 100, DateTime.UtcNow.AddDays(-9));
        new RenderCache(_dir.File("c"), () => new HashSet<string>(), maxBytes: 10 * 1024 * 1024).Evict();

        File.Exists(a).ShouldBeTrue();
    }

    [Fact]
    public void Default_cap_is_750_megabytes() => RenderCache.DefaultMaxBytes.ShouldBe(750L * 1024 * 1024);

    [Fact]
    public void Missing_directory_is_not_an_error()
    {
        var cache = new RenderCache(_dir.File("nope"), () => new HashSet<string>());

        Should.NotThrow(() => cache.Evict());
        cache.TotalBytes().ShouldBe(0);
    }
}
