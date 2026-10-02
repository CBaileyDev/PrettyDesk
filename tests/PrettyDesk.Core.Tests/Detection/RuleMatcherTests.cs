using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Settings;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;
using static PrettyDesk.Core.Tests.Support.TestData;

namespace PrettyDesk.Core.Tests.Detection;

public class RuleMatcherTests
{
    [Fact]
    public void Background_process_scan_keeps_temporary_allocations_small()
    {
        var games = Enumerable.Range(0, 46).Select(i => Game($"g{i}", configure: b => b.Exe.Add($"g{i}.exe"))).ToArray();
        var matcher = new RuleMatcher(Registry(games), null);
        var processes = Enumerable.Range(0, 1000).Select(i => Proc(i + 1, "background.exe")).ToArray();
        matcher.Match(processes, 0);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var matches = matcher.Match(processes, 0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        matches.ShouldBeEmpty();
        allocated.ShouldBeLessThan(10_000);
    }

    private static IReadOnlyList<GameMatch> Run(GameRegistry registry, FakeDetails? details, uint steam, params ProcessInfo[] processes) =>
        new RuleMatcher(registry, details).Match(processes, steam);

    [Theory]
    [InlineData("cs2.exe")]
    [InlineData("CS2.EXE")]
    [InlineData("Cs2.Exe")]
    public void Exe_name_match_is_case_insensitive(string running)
    {
        var registry = Registry([Game("cs2", configure: b => b.Exe.Add("cs2.exe"))]);

        Run(registry, null, 0, Proc(10, running)).ShouldHaveSingleItem().GameId.ShouldBe("cs2");
    }

    [Fact]
    public void Exe_name_must_match_exactly_not_as_a_substring()
    {
        var registry = Registry([Game("cs2", configure: b => b.Exe.Add("cs2.exe"))]);

        Run(registry, null, 0, Proc(1, "cs2.exe.bak"), Proc(2, "notcs2.exe")).ShouldBeEmpty();
    }

    [Fact]
    public void All_pids_of_a_game_are_reported()
    {
        var registry = Registry([Game("g", configure: b => b.Exe.Add("g.exe"))]);

        Run(registry, null, 0, Proc(5, "g.exe"), Proc(6, "g.exe"), Proc(7, "other.exe"))
            .ShouldHaveSingleItem().Pids.ShouldBe([5, 6]);
    }

    [Fact]
    public void Steam_app_id_matches_without_any_process()
    {
        var registry = Registry([Game("cs2", configure: b => b.Steam.Add(730))]);

        var match = Run(registry, null, 730).ShouldHaveSingleItem();
        match.ViaSteam.ShouldBeTrue();
        match.Pids.ShouldBeEmpty();
    }

    [Fact]
    public void Steam_app_id_zero_never_matches()
    {
        var registry = Registry([Game("cs2", configure: b => b.Steam.Add(730))]);

        Run(registry, null, 0).ShouldBeEmpty();
    }

    [Fact]
    public void Steam_flip_between_games_is_reflected()
    {
        var registry = Registry([Game("a", configure: b => b.Steam.Add(1)), Game("b", configure: b => b.Steam.Add(2))]);

        Run(registry, null, 1).ShouldHaveSingleItem().GameId.ShouldBe("a");
        Run(registry, null, 2).ShouldHaveSingleItem().GameId.ShouldBe("b");
    }

    [Fact]
    public void Game_exclude_list_blocks_a_match()
    {
        var registry = Registry([Game("g", configure: b => { b.Exe.Add("g.exe"); b.Exclude.Add("g.exe"); })]);

        Run(registry, null, 0, Proc(1, "g.exe")).ShouldBeEmpty();
    }

    [Fact]
    public void Global_launcher_excludes_never_match_even_if_a_rule_lists_them()
    {
        var registry = Registry([Game("oops", configure: b => b.Exe.Add("steam.exe"))], excludes: ["steam.exe"]);

        Run(registry, null, 0, Proc(1, "steam.exe")).ShouldBeEmpty();
    }

    [Fact]
    public void Path_rule_requires_the_path_to_contain_a_fragment()
    {
        var registry = Registry([Game("g", configure: b => { b.Exe.Add("game.exe"); b.Path.Add("\\Steam\\"); })]);
        var details = new FakeDetails();
        details.Paths[1] = @"D:\Steam\steamapps\common\Game\game.exe";
        details.Paths[2] = @"C:\Temp\game.exe";

        Run(registry, details, 0, Proc(1, "game.exe")).ShouldHaveSingleItem();
        Run(registry, details, 0, Proc(2, "game.exe")).ShouldBeEmpty();
    }

    [Fact]
    public void Path_rule_falls_back_to_exe_name_when_access_is_denied()
    {
        var registry = Registry([Game("g", configure: b => { b.Exe.Add("game.exe"); b.Path.Add("\\Steam\\"); })]);
        var details = new FakeDetails(); // TryGetImagePath returns null = protected process

        Run(registry, details, 0, Proc(1, "game.exe")).ShouldHaveSingleItem();
    }

    [Fact]
    public void Path_is_not_queried_for_processes_that_do_not_match_by_name()
    {
        var registry = Registry([Game("g", configure: b => { b.Exe.Add("game.exe"); b.Path.Add("x"); })]);
        var details = new FakeDetails();

        Run(registry, details, 0, Proc(1, "other.exe"), Proc(2, "svchost.exe"));

        details.PathCalls.ShouldBe(0);
        details.TitleCalls.ShouldBe(0);
    }

    [Fact]
    public void Games_without_secondary_rules_never_trigger_lookups()
    {
        var registry = Registry([Game("g", configure: b => b.Exe.Add("game.exe"))]);
        var details = new FakeDetails();

        Run(registry, details, 0, Proc(1, "game.exe"));

        details.PathCalls.ShouldBe(0);
        details.TitleCalls.ShouldBe(0);
    }

    [Fact]
    public void Window_title_rule_distinguishes_minecraft_from_other_java_apps()
    {
        var registry = Registry(
            [Game("minecraft", configure: b => { b.Exe.Add("javaw.exe"); b.Title.Add("Minecraft"); })],
            excludes: ["javaw.exe"]);
        var details = new FakeDetails();
        details.Titles[1] = "Minecraft 1.21.4 - Singleplayer";
        details.Titles[2] = "IntelliJ IDEA";
        details.Titles[3] = null;

        Run(registry, details, 0, Proc(1, "javaw.exe")).ShouldHaveSingleItem().GameId.ShouldBe("minecraft");
        Run(registry, details, 0, Proc(2, "javaw.exe")).ShouldBeEmpty();
        Run(registry, details, 0, Proc(3, "javaw.exe")).ShouldBeEmpty();
    }

    [Fact]
    public void Javaw_without_a_title_rule_stays_excluded()
    {
        var registry = Registry([Game("g", configure: b => b.Exe.Add("javaw.exe"))], excludes: ["javaw.exe"]);

        Run(registry, new FakeDetails(), 0, Proc(1, "javaw.exe")).ShouldBeEmpty();
    }

    [Fact]
    public void Second_matching_process_is_found_when_the_first_fails_secondary_rules()
    {
        var registry = Registry([Game("m", configure: b => { b.Exe.Add("javaw.exe"); b.Title.Add("Minecraft"); })]);
        var details = new FakeDetails();
        details.Titles[1] = "Some IDE";
        details.Titles[2] = "Minecraft";

        Run(registry, details, 0, Proc(1, "javaw.exe"), Proc(2, "javaw.exe")).ShouldHaveSingleItem().Pids.ShouldBe([2]);
    }
}

public class GameRegistryTests
{
    [Fact]
    public void Disabled_games_are_not_matched()
    {
        var settings = new AppSettings();
        settings.Games["a"] = new GameSettings { Enabled = false };
        var registry = Registry([Game("a", configure: b => b.Exe.Add("a.exe")), Game("b", configure: b => b.Exe.Add("b.exe"))], settings: settings);

        registry.Games.Select(g => g.Id).ShouldBe(["b"]);
    }

    [Fact]
    public void Custom_rules_win_over_catalog_rules_for_the_same_exe()
    {
        var settings = new AppSettings();
        settings.CustomGames.Add(new CustomGame { Id = "custom-1", DisplayName = "Mine", ExeNames = ["Shared.exe"] });
        var registry = Registry([Game("cat", configure: b => { b.Exe.Add("shared.exe"); b.Exe.Add("cat.exe"); })], settings: settings);

        var matcher = new RuleMatcher(registry, null);

        matcher.Match([Proc(1, "shared.exe")], 0).ShouldHaveSingleItem().GameId.ShouldBe("custom-1");
        matcher.Match([Proc(2, "cat.exe")], 0).ShouldHaveSingleItem().GameId.ShouldBe("cat");
    }

    [Fact]
    public void Catalog_game_whose_every_exe_is_claimed_by_a_custom_game_is_dropped()
    {
        var settings = new AppSettings();
        settings.CustomGames.Add(new CustomGame { Id = "c", DisplayName = "C", ExeNames = ["only.exe"] });
        var registry = Registry([Game("cat", configure: b => b.Exe.Add("only.exe"))], settings: settings);

        registry.Games.Select(g => g.Id).ShouldBe(["c"]);
    }

    [Fact]
    public void Custom_game_can_be_disabled_like_any_game()
    {
        var settings = new AppSettings();
        settings.CustomGames.Add(new CustomGame { Id = "c", DisplayName = "C", ExeNames = ["c.exe"] });
        settings.Games["c"] = new GameSettings { Enabled = false };

        Registry([], settings: settings).Games.ShouldBeEmpty();
    }
}
