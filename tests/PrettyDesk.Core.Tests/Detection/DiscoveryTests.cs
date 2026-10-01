using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Detection.Discovery;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;
using static PrettyDesk.Core.Tests.Support.TestData;

namespace PrettyDesk.Core.Tests.Detection;

public class VdfParserTests
{
    [Fact]
    public void Parses_nested_blocks_and_escaped_windows_paths()
    {
        var root = VdfParser.Parse("""
            "libraryfolders"
            {
                "0" { "path" "C:\\Program Files (x86)\\Steam" "apps" { "730" "123" } }
                "1" { "path" "D:\\Games\\SteamLibrary" }
            }
            """);

        var folders = root["libraryfolders"]!;
        folders["0"]!.Text("path").ShouldBe(@"C:\Program Files (x86)\Steam");
        folders["0"]!["apps"]!.Text("730").ShouldBe("123");
        folders["1"]!.Text("path").ShouldBe(@"D:\Games\SteamLibrary");
    }

    [Fact]
    public void Keys_are_case_insensitive_and_comments_are_skipped()
    {
        var root = VdfParser.Parse("// header\n\"AppState\" { \"AppID\" \"1\" // trailing\n }");

        root["appstate"]!.Text("appid").ShouldBe("1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"unterminated")]
    [InlineData("\"a\" {")]
    [InlineData("}}}{{{")]
    [InlineData("\"key\"")]
    public void Garbage_never_throws(string text) => Should.NotThrow(() => VdfParser.Parse(text));

    [Fact]
    public void Escaped_quotes_survive()
    {
        VdfParser.Parse("\"name\" \"The \\\"Game\\\"\"").Text("name").ShouldBe("The \"Game\"");
    }

    [Fact]
    public void Absurd_nesting_is_cut_off()
    {
        var deep = string.Concat(Enumerable.Repeat("\"k\" {", 200));

        Should.NotThrow(() => VdfParser.Parse(deep));
    }
}

public sealed class InstalledGameScannerTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static void Manifest(string library, uint appId, string name, string installDir, int flags = 4)
    {
        var apps = Path.Combine(library, "steamapps");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, $"appmanifest_{appId}.acf"),
            $"\"AppState\"\n{{\n\t\"appid\"\t\"{appId}\"\n\t\"name\"\t\"{name}\"\n\t\"installdir\"\t\"{installDir}\"\n\t\"StateFlags\"\t\"{flags}\"\n}}");
    }

    [Fact]
    public void Finds_steam_games_across_library_folders()
    {
        var steam = _dir.File("Steam");
        var second = _dir.File("Library2");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"" + steam.Replace("\\", "\\\\", StringComparison.Ordinal) + "\" } \"1\" { \"path\" \"" + second.Replace("\\", "\\\\", StringComparison.Ordinal) + "\" } }");
        Manifest(steam, 730, "Counter-Strike 2", "CS2");
        Manifest(second, 570, "Dota 2", "dota 2 beta");

        var games = new InstalledGameScanner(steam, null).Scan();

        games.Select(g => g.SteamAppId).ShouldBe([730u, 570u], ignoreOrder: true);
        games.First(g => g.SteamAppId == 570).InstallPath.ShouldBe(Path.Combine(second, "steamapps", "common", "dota 2 beta"));
        games.ShouldAllBe(g => g.Source == "steam");
    }

    [Fact]
    public void Half_installed_steam_games_are_ignored()
    {
        var steam = _dir.File("Steam");
        Manifest(steam, 730, "CS2", "CS2", flags: 1026);

        new InstalledGameScanner(steam, null).Scan().ShouldBeEmpty();
    }

    [Fact]
    public void Same_game_listed_by_two_libraries_is_reported_once()
    {
        var steam = _dir.File("Steam");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" { \"path\" \"" + steam.Replace("\\", "\\\\", StringComparison.Ordinal) + "\" } }");
        Manifest(steam, 730, "CS2", "CS2");

        new InstalledGameScanner(steam, null).Scan().Count.ShouldBe(1);
    }

    [Fact]
    public void Finds_epic_games_and_skips_incomplete_or_corrupt_manifests()
    {
        var epic = _dir.File("Manifests");
        Directory.CreateDirectory(epic);
        File.WriteAllText(Path.Combine(epic, "a.item"), """{ "DisplayName": "Fortnite", "InstallLocation": "C:\\Epic\\Fortnite", "LaunchExecutable": "FortniteGame\\Binaries\\Win64\\FortniteClient-Win64-Shipping.exe" }""");
        File.WriteAllText(Path.Combine(epic, "b.item"), """{ "DisplayName": "Half", "InstallLocation": "C:\\x", "bIsIncompleteInstall": true }""");
        File.WriteAllText(Path.Combine(epic, "c.item"), "{ not json");
        File.WriteAllText(Path.Combine(epic, "d.item"), "[1,2]");
        File.WriteAllText(Path.Combine(epic, "e.item"), """{ "DisplayName": "NoPath" }""");

        var games = new InstalledGameScanner(null, epic).Scan();

        games.ShouldHaveSingleItem().DisplayName.ShouldBe("Fortnite");
    }

    [Fact]
    public void Missing_launchers_are_not_errors()
    {
        new InstalledGameScanner(null, null).Scan().ShouldBeEmpty();
        new InstalledGameScanner(_dir.File("nope"), _dir.File("nope2")).Scan().ShouldBeEmpty();
    }

    [Fact]
    public void Catalog_matching_uses_steam_app_ids_and_epic_exe_hints()
    {
        var catalog = new CatalogDocument
        {
            Games =
            [
                Game("cs2", configure: b => { b.Exe.Add("cs2.exe"); b.Steam.Add(730); }),
                Game("fortnite", configure: b => b.Exe.Add("FortniteClient-Win64-Shipping.exe")),
                Game("rust", configure: b => b.Exe.Add("RustClient.exe")),
            ],
        };
        var installed = new[]
        {
            new InstalledGame("steam", "x", null, 730, "CS2"),
            new InstalledGame("epic", "y", @"FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe", null, "Fortnite"),
        };

        InstalledGameScanner.MatchCatalog(catalog, installed).Select(g => g.Id).ShouldBe(["cs2", "fortnite"]);
    }
}

public class UnknownGameHinterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);
    private readonly UnknownGameHinter _hinter = new();

    private UnknownGameHint? At(double seconds, string? exe = "indie.exe", UserNotificationState state = UserNotificationState.RunningD3DFullScreen, bool known = false, bool enabled = true) =>
        _hinter.Observe(T0 + TimeSpan.FromSeconds(seconds), state, exe, known, enabled);

    [Fact]
    public void Hints_after_sixty_seconds_of_fullscreen_for_an_unknown_exe()
    {
        At(0).ShouldBeNull();
        At(59).ShouldBeNull();

        At(60).ShouldNotBeNull().ExeName.ShouldBe("indie.exe");
    }

    [Fact]
    public void Offers_each_exe_only_once()
    {
        At(0);
        At(60).ShouldNotBeNull();

        At(61).ShouldBeNull();
        At(200).ShouldBeNull();
        At(300).ShouldBeNull();
    }

    [Fact]
    public void Busy_state_counts_too()
    {
        At(0, state: UserNotificationState.Busy);
        At(61, state: UserNotificationState.Busy).ShouldNotBeNull();
    }

    [Fact]
    public void Leaving_fullscreen_resets_the_timer()
    {
        At(0);
        At(40);
        At(45, state: UserNotificationState.AcceptsNotifications);
        At(50);

        At(100).ShouldBeNull();
        At(110).ShouldNotBeNull();
    }

    [Fact]
    public void Switching_to_another_app_resets_the_timer()
    {
        At(0, "a.exe");
        At(50, "b.exe");

        At(70, "b.exe").ShouldBeNull();
        At(110, "b.exe").ShouldNotBeNull().ExeName.ShouldBe("b.exe");
    }

    [Theory]
    [InlineData("chrome.exe")]
    [InlineData("MSEDGE.EXE")]
    [InlineData("vlc.exe")]
    [InlineData("POWERPNT.EXE")]
    [InlineData("explorer.exe")]
    public void Everyday_fullscreen_apps_never_trigger_a_hint(string exe)
    {
        At(0, exe);

        At(600, exe).ShouldBeNull();
    }

    [Fact]
    public void Known_games_and_launchers_never_trigger_a_hint()
    {
        At(0, known: true);

        At(600, known: true).ShouldBeNull();
    }

    [Fact]
    public void Disabled_in_settings_means_never()
    {
        At(0, enabled: false);

        At(600, enabled: false).ShouldBeNull();
    }

    [Fact]
    public void Hints_from_previous_runs_are_remembered()
    {
        _hinter.MarkHinted(["indie.exe"]);
        At(0);

        At(600).ShouldBeNull();
    }

    [Fact]
    public void No_foreground_app_is_ignored()
    {
        At(0, exe: null);

        At(600, exe: null).ShouldBeNull();
    }
}
