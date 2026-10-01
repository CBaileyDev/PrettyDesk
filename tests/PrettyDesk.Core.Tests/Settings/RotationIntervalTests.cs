using PrettyDesk.Core.Settings;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Settings;

public class RotationIntervalTests
{
    [Theory]
    [InlineData("PT5M", 5)]
    [InlineData("PT30M", 30)]
    [InlineData("PT1H", 60)]
    [InlineData("P1D", 1440)]
    [InlineData("pt6h", 360)]
    public void Parses_iso_durations(string text, int minutes) =>
        RotationInterval.Parse(text).Duration.ShouldBe(TimeSpan.FromMinutes(minutes));

    [Theory]
    [InlineData("unlock", RotationIntervalKind.Unlock)]
    [InlineData("session", RotationIntervalKind.Session)]
    [InlineData("never", RotationIntervalKind.Never)]
    [InlineData("UNLOCK", RotationIntervalKind.Unlock)]
    public void Parses_keywords(string text, RotationIntervalKind kind) =>
        RotationInterval.Parse(text).Kind.ShouldBe(kind);

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("30 minutes")]
    public void Rejects_garbage(string text) => RotationInterval.TryParse(text, out _).ShouldBeFalse();

    [Fact]
    public void Clamps_hand_edited_values_to_the_documented_range()
    {
        RotationInterval.Parse("PT10S").Duration.ShouldBe(TimeSpan.FromMinutes(1));
        RotationInterval.Parse("P30D").Duration.ShouldBe(TimeSpan.FromDays(7));
    }

    [Fact]
    public void Every_rejects_out_of_range() =>
        Should.Throw<ArgumentOutOfRangeException>(() => RotationInterval.Every(TimeSpan.FromSeconds(30)));

    [Fact]
    public void Format_round_trips() =>
        RotationInterval.Parse(RotationInterval.Every(TimeSpan.FromHours(3)).ToString()).ShouldBe(RotationInterval.Every(TimeSpan.FromHours(3)));

    [Fact]
    public void Presets_match_fr_wp_3() =>
        RotationInterval.Presets.Count.ShouldBe(8);

    [Theory]
    [InlineData("PT5M", "Every 5 minutes")]
    [InlineData("PT1H", "Every hour")]
    [InlineData("PT3H", "Every 3 hours")]
    [InlineData("P1D", "Daily")]
    [InlineData("unlock", "On every unlock")]
    public void Human_strings(string text, string expected) => RotationInterval.Parse(text).ToHumanString().ShouldBe(expected);
}
