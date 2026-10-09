using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace PrettyDesk.Core.Tests.Content;

public sealed class ToneClassifierTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(0, Tones.Dark)]
    [InlineData(99.9, Tones.Dark)]
    [InlineData(100, Tones.Mid)]
    [InlineData(159.9, Tones.Mid)]
    [InlineData(160, Tones.Light)]
    [InlineData(255, Tones.Light)]
    public void Mean_luma_thresholds_split_dark_mid_and_light(double meanLuma, string expected)
    {
        ToneClassifier.FromMeanLuma(meanLuma).ShouldBe(expected);
    }

    [Fact]
    public void A_near_black_image_is_dark_a_white_image_is_light_and_a_grey_one_is_mid()
    {
        Measure(new SKColor(8, 8, 12)).ShouldBe(Tones.Dark);
        Measure(new SKColor(250, 248, 244)).ShouldBe(Tones.Light);
        Measure(new SKColor(128, 128, 128)).ShouldBe(Tones.Mid);
    }

    [Fact]
    public void Luma_follows_perception_so_pure_green_is_brighter_than_pure_blue()
    {
        using var green = Solid(new SKColor(0, 255, 0));
        using var blue = Solid(new SKColor(0, 0, 255));

        ToneClassifier.MeanLuma(green).ShouldBeGreaterThan(ToneClassifier.MeanLuma(blue) * 4);
    }

    [Fact]
    public void A_file_that_cannot_be_decoded_is_mid_tone_never_silently_dark()
    {
        var path = _dir.File("broken.png");
        File.WriteAllText(path, "not an image");

        ToneClassifier.Measure(path).ShouldBe(Tones.Mid);
    }

    private string Measure(SKColor color)
    {
        var path = _dir.File($"{Guid.NewGuid():N}.png");
        ImageFactory.Write(path, 320, 180, background: color);
        return ToneClassifier.Measure(path);
    }

    private static SKBitmap Solid(SKColor color)
    {
        var bitmap = new SKBitmap(64, 36);
        bitmap.Erase(color);
        return bitmap;
    }
}

public sealed class UserImageToneTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly UserImageStore _store;

    public UserImageToneTests() => _store = new UserImageStore(_dir.File("user"));

    public void Dispose() => _dir.Dispose();

    private string Source(string name, SKColor background)
    {
        var path = _dir.File(name);
        ImageFactory.Write(path, 1600, 900, background: background);
        return path;
    }

    [Fact]
    public void Importing_measures_the_tone_and_remembers_it_beside_the_image()
    {
        var result = _store.Import(Source("night.png", new SKColor(10, 12, 20)));

        result.Image.ShouldNotBeNull();
        result.Image!.Tone.ShouldBe(Tones.Dark);
        File.Exists(UserImageStore.ToneSidecarPath(result.Image.Path)).ShouldBeTrue();
        File.ReadAllText(UserImageStore.ToneSidecarPath(result.Image.Path)).ShouldBe(Tones.Dark);
    }

    [Fact]
    public void Listing_reports_each_image_with_its_own_tone()
    {
        _store.Import(Source("night.png", new SKColor(10, 12, 20)));
        _store.Import(Source("snow.png", new SKColor(240, 242, 245)));

        var tones = _store.List().Select(i => i.Tone).OrderBy(t => t, StringComparer.Ordinal).ToList();

        tones.ShouldBe([Tones.Dark, Tones.Light]);
    }

    [Fact]
    public void A_remembered_tone_is_used_instead_of_measuring_again()
    {
        var imported = _store.Import(Source("cached.png", new SKColor(10, 12, 20))).Image!;
        File.WriteAllText(UserImageStore.ToneSidecarPath(imported.Path), Tones.Light);

        _store.TryGet(imported.Id)!.Tone.ShouldBe(Tones.Light);
    }

    [Fact]
    public void A_missing_or_garbled_sidecar_is_measured_again_and_repaired()
    {
        var imported = _store.Import(Source("repair.png", new SKColor(10, 12, 20))).Image!;
        File.WriteAllText(UserImageStore.ToneSidecarPath(imported.Path), "neon");

        _store.TryGet(imported.Id)!.Tone.ShouldBe(Tones.Dark);
        File.ReadAllText(UserImageStore.ToneSidecarPath(imported.Path)).ShouldBe(Tones.Dark);
    }

    [Fact]
    public void Removing_an_image_removes_its_tone_sidecar()
    {
        var imported = _store.Import(Source("gone.png", new SKColor(10, 12, 20))).Image!;

        _store.Remove(imported.Id).ShouldBeTrue();

        File.Exists(UserImageStore.ToneSidecarPath(imported.Path)).ShouldBeFalse();
        File.Exists(imported.Path).ShouldBeFalse();
    }
}
