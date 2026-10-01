using System.Globalization;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Presentation.Formatting;
using PrettyDesk.Presentation.Resources;
using Shouldly;
using Xunit;

namespace PrettyDesk.Presentation.Tests.Formatting;

public class FormatterTests
{
    public FormatterTests() => Strings.Culture = CultureInfo.GetCultureInfo("en-US");

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    [InlineData(1_288_490_189L, "1.2 GB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void Byte_sizes_are_human_readable(long bytes, string expected) => ByteSize.Format(bytes).ShouldBe(expected);

    [Theory]
    [InlineData(-5, "in under a minute")]
    [InlineData(30, "in under a minute")]
    [InlineData(60, "in 1 min")]
    [InlineData(12 * 60, "in 12 min")]
    [InlineData(59 * 60 + 40, "in 1 h")]
    [InlineData(65 * 60, "in 1 h 5 min")]
    [InlineData(3 * 3600, "in 3 h")]
    [InlineData(24 * 3600, "in 1 day")]
    [InlineData(3 * 24 * 3600, "in 3 days")]
    public void Relative_times_read_naturally(int seconds, string expected) =>
        RelativeTime.Format(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);

    [Fact]
    public void Unknown_status_reads_as_starting() => StatusFormatter.Describe(null, Now).Headline.ShouldBe("Starting…");

    [Fact]
    public void Default_rotating_shows_collection_and_next_change()
    {
        var text = StatusFormatter.Describe(new OrchestratorStatus { Mode = OrchestratorMode.Default, DefaultTitle = "Matte Black", IsRotating = true, NextChange = Now + TimeSpan.FromMinutes(12) }, Now);

        text.Headline.ShouldBe("Default · Matte Black (rotating)");
        text.Detail.ShouldBe("Next change in 12 min");
    }

    [Fact]
    public void Default_fixed_has_no_next_change()
    {
        var text = StatusFormatter.Describe(new OrchestratorStatus { DefaultTitle = "Clean White", IsRotating = false, NextChange = Now }, Now);

        text.Headline.ShouldBe("Default · Clean White");
        text.Detail.ShouldBeNull();
    }

    [Fact]
    public void Game_shows_position_when_the_pool_has_several_wallpapers()
    {
        var text = StatusFormatter.Describe(new OrchestratorStatus { Mode = OrchestratorMode.Game, GameName = "VALORANT", PoolIndex = 1, PoolSize = 4 }, Now);

        text.Headline.ShouldBe("Playing: VALORANT");
        text.Detail.ShouldBe("Wallpaper 2 of 4");
    }

    [Fact]
    public void Game_with_one_wallpaper_has_no_position_detail() =>
        StatusFormatter.Describe(new OrchestratorStatus { Mode = OrchestratorMode.Game, GameName = "X", PoolSize = 1 }, Now).Detail.ShouldBeNull();

    [Fact]
    public void Game_grace_says_it_is_returning_to_default()
    {
        var text = StatusFormatter.Describe(new OrchestratorStatus { Mode = OrchestratorMode.GameGrace, GameName = "X" }, Now);

        text.Detail.ShouldBe("Returning to your default wallpaper soon");
    }

    [Fact]
    public void Loading_game_uses_the_getting_wallpapers_wording()
    {
        var text = StatusFormatter.Describe(new OrchestratorStatus { Mode = OrchestratorMode.GameLoading, GameName = "Dota 2" }, Now);

        text.Headline.ShouldBe("Getting Dota 2 wallpapers…");
        text.Detail.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Paused_indefinitely_and_until_a_time()
    {
        StatusFormatter.Describe(new OrchestratorStatus { Mode = OrchestratorMode.Paused }, Now).Headline.ShouldBe("Paused");

        var until = StatusFormatter.Describe(new OrchestratorStatus { Mode = OrchestratorMode.Paused, PausedUntil = Now + TimeSpan.FromHours(1) }, Now, TimeZoneInfo.Utc);
        // ICU versions differ on the space before "PM" (regular vs narrow no-break), so assert the stable parts.
        until.Headline.ShouldStartWith("Paused until 1:00");
        until.Headline.ShouldEndWith("PM");
    }

    [Fact]
    public void Blocked_explains_the_organization_policy()
    {
        var text = StatusFormatter.Describe(new OrchestratorStatus { Mode = OrchestratorMode.Blocked }, Now);

        text.Headline.ShouldBe("Your organization manages your wallpaper");
    }

    [Fact]
    public void No_content_is_a_designed_message_not_a_blank()
    {
        var text = StatusFormatter.Describe(new OrchestratorStatus { NoContent = true }, Now);

        text.Headline.ShouldBe("No wallpapers are available yet");
        text.Detail.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Failure_is_reported_in_human_words()
    {
        var text = StatusFormatter.Describe(new OrchestratorStatus { DefaultTitle = "X", ApplyFailed = true }, Now);

        text.Detail.ShouldBe("Couldn't change the wallpaper. Trying again…");
    }

    [Fact]
    public void Preview_status_is_labelled()
    {
        StatusFormatter.Describe(new OrchestratorStatus { Mode = OrchestratorMode.Preview }, Now).Headline.ShouldBe("Previewing a wallpaper");
    }
}
