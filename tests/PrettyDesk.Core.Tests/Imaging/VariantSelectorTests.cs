using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Imaging;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Imaging;

public class VariantSelectorTests
{
    private static readonly Dictionary<string, double> AllVariants = Variants.All.ToDictionary(v => v.Key, v => v.Ratio);

    [Theory]
    [InlineData(1366, 768, "16x9")]
    [InlineData(1920, 1080, "16x9")]
    [InlineData(1920, 1200, "16x10")]
    [InlineData(2256, 1504, "3x2")]
    [InlineData(2560, 1080, "21x9")]
    [InlineData(2560, 1440, "16x9")]
    [InlineData(3440, 1440, "21x9")]
    [InlineData(3840, 2160, "16x9")]
    [InlineData(5120, 1440, "32x9")]
    [InlineData(5120, 2160, "21x9")]
    [InlineData(1080, 1920, "9x16")]
    [InlineData(2160, 3840, "9x16")]
    [InlineData(2560, 1600, "16x10")]
    [InlineData(2880, 1920, "3x2")]
    [InlineData(1280, 1024, "3x2")]
    public void Picks_the_closest_ratio_for_the_spec_monitor_table(int w, int h, string expected)
    {
        var choice = VariantSelector.Select(w, h, AllVariants).ShouldNotBeNull();

        choice.Key.ShouldBe(expected);
        choice.IsFallback.ShouldBeFalse();
    }

    [Fact]
    public void Missing_variant_falls_back_to_the_next_closest()
    {
        var onlyLandscape = new Dictionary<string, double> { ["16x9"] = 16.0 / 9, ["16x10"] = 1.6 };

        var choice = VariantSelector.Select(2560, 1080, onlyLandscape).ShouldNotBeNull();

        choice.Key.ShouldBe("16x9");
        choice.IsFallback.ShouldBeTrue();
        choice.QualityNotice.ShouldNotBeNull();
    }

    [Fact]
    public void Starter_set_with_only_16x9_serves_every_monitor_shape()
    {
        var starter = new Dictionary<string, double> { ["16x9"] = 16.0 / 9 };

        foreach (var (w, h) in new[] { (1920, 1200), (2256, 1504), (3440, 1440), (5120, 1440), (1080, 1920) })
        {
            VariantSelector.Select(w, h, starter)!.Key.ShouldBe("16x9");
        }
    }

    [Fact]
    public void Portrait_without_9x16_crops_the_16x9_even_when_3x2_is_closer()
    {
        var available = new Dictionary<string, double> { ["16x9"] = 16.0 / 9, ["3x2"] = 1.5 };

        var choice = VariantSelector.Select(1080, 1920, available).ShouldNotBeNull();

        choice.Key.ShouldBe("16x9");
        choice.QualityNotice.ShouldNotBeNull();
    }

    [Fact]
    public void User_image_uses_its_own_native_ratio()
    {
        var variants = new Dictionary<string, LocalVariant> { ["user"] = new LocalVariant("user", "/x.png", 3000, 2000) };

        VariantSelector.Select(1920, 1080, variants)!.Key.ShouldBe("user");
    }

    [Fact]
    public void Nothing_available_returns_null()
    {
        VariantSelector.Select(1920, 1080, new Dictionary<string, double>()).ShouldBeNull();
        VariantSelector.Select(0, 0, AllVariants).ShouldBeNull();
    }
}

public class FocalCropperTests
{
    [Fact]
    public void Same_ratio_returns_the_whole_image()
    {
        var crop = FocalCropper.Compute(3840, 2160, 16.0 / 9, new FocalPoint(0.2, 0.9));

        crop.ShouldBe(new CropRect(0, 0, 3840, 2160));
    }

    [Fact]
    public void Narrower_target_crops_width_around_the_focal_x()
    {
        var crop = FocalCropper.Compute(3840, 2160, 1.5, new FocalPoint(0.5, 0.5));

        crop.Height.ShouldBe(2160);
        crop.Width.ShouldBe(3240, 0.001);
        crop.X.ShouldBe(300, 0.001);
    }

    [Fact]
    public void Focal_near_the_edge_clamps_to_the_image()
    {
        var crop = FocalCropper.Compute(3840, 2160, 1.5, new FocalPoint(1, 0.5));

        (crop.X + crop.Width).ShouldBe(3840, 0.001);
        FocalCropper.Compute(3840, 2160, 1.5, new FocalPoint(0, 0.5)).X.ShouldBe(0);
    }

    [Fact]
    public void Wider_target_crops_height_around_the_focal_y()
    {
        var crop = FocalCropper.Compute(3840, 2160, 3440.0 / 1440, new FocalPoint(0.5, 0.2));

        crop.Width.ShouldBe(3840);
        crop.Height.ShouldBe(3840 / (3440.0 / 1440), 0.01);
        crop.Y.ShouldBe(0, 0.001);
    }

    [Theory]
    [InlineData(-3, 7)]
    [InlineData(2, -1)]
    public void Out_of_range_focal_values_are_clamped(double x, double y)
    {
        var crop = FocalCropper.Compute(3840, 2160, 1.5, new FocalPoint(x, y));

        crop.X.ShouldBeInRange(0, 3840 - crop.Width);
        crop.Y.ShouldBeInRange(0, 2160 - crop.Height);
    }

    [Fact]
    public void Portrait_crop_from_landscape_keeps_full_height()
    {
        var crop = FocalCropper.Compute(3840, 2160, 9.0 / 16, FocalPoint.Center);

        crop.Height.ShouldBe(2160);
        crop.Width.ShouldBe(2160 * 9.0 / 16, 0.001);
    }
}
