using PrettyDesk.Core.Privacy;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Detection;

public class LogScrubberTests
{
    private const string Profile = @"C:\Users\jane.doe";
    private const string Local = @"C:\Users\jane.doe\AppData\Local";

    [Fact]
    public void Profile_paths_are_abstracted_and_local_app_data_is_kept_readable()
    {
        var text = @"Loaded C:\Users\jane.doe\AppData\Local\PrettyDesk\settings.json and C:\Users\jane.doe\Pictures\wall.png";

        var scrubbed = LogScrubber.Scrub(text, "jane.doe", Profile, Local);

        scrubbed.ShouldBe(@"Loaded %LOCALAPPDATA%\PrettyDesk\settings.json and %USERPROFILE%\Pictures\wall.png");
    }

    [Fact]
    public void Forward_slash_and_escaped_variants_are_scrubbed_too()
    {
        LogScrubber.Scrub("C:/Users/jane.doe/x", "jane.doe", Profile).ShouldBe("%USERPROFILE%/x");
        LogScrubber.Scrub(@"C:\\Users\\jane.doe\\x", "jane.doe", Profile).ShouldBe(@"%USERPROFILE%\\x");
    }

    [Fact]
    public void Any_remaining_mention_of_the_user_name_is_replaced()
    {
        LogScrubber.Scrub(@"D:\Users\JANE.DOE\file and \\server\share\jane.doe\x", "jane.doe", Profile).ShouldNotContain("jane.doe", Case.Insensitive);
    }

    [Fact]
    public void Text_without_personal_data_is_unchanged()
    {
        LogScrubber.Scrub("Orchestrator Default -> Game (game: cs2)", "jane.doe", Profile).ShouldBe("Orchestrator Default -> Game (game: cs2)");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void Empty_text_and_very_short_user_names_are_handled(string userName)
    {
        Should.NotThrow(() => LogScrubber.Scrub("", userName, Profile));
        LogScrubber.Scrub("abc", userName, Profile).ShouldBe("abc");
    }
}
