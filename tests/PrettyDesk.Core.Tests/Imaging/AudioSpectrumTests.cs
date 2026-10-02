using PrettyDesk.Core.Imaging;
using PrettyDesk.Core.Orchestration;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Imaging;

public sealed class AudioSpectrumTests
{
    [Fact]
    public void Silence_and_invalid_samples_produce_finite_bounded_levels()
    {
        AudioSpectrum.Analyze(new float[1024], 48000).ShouldAllBe(v => v == 0);
        AudioSpectrum.Analyze([float.NaN, float.PositiveInfinity, float.NegativeInfinity], 48000).ShouldAllBe(v => double.IsFinite(v) && v >= 0 && v <= 1);
    }

    [Fact]
    public void A_test_tone_is_strongest_at_its_frequency()
    {
        const int band = 6;
        var frequency = 80 * Math.Pow(100, (double)band / (AudioSpectrum.BandCount - 1));
        var samples = Enumerable.Range(0, 1024).Select(i => (float)(0.25 * Math.Sin(2 * Math.PI * frequency * i / 48000))).ToArray();
        var levels = AudioSpectrum.Analyze(samples, 48000);
        levels[band].ShouldBeGreaterThan(0.5);
        levels[band].ShouldBe(levels.Max());
    }

    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, false, true)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    public void Desktop_surface_visibility_respects_each_stop_condition(bool enabled, bool game, bool locked, bool saver, bool expected) =>
        DesktopSurfacePolicy.ShouldShow(enabled, game, locked, saver).ShouldBe(expected);
}
