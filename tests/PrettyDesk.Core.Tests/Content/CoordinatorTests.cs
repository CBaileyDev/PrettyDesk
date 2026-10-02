using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Settings;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;
using static PrettyDesk.Core.Tests.Support.TestData;

namespace PrettyDesk.Core.Tests.Content;

public sealed class PrefetchCoordinatorTests : IDisposable
{
    private readonly FakeSettings _settings = new();
    private readonly FakeMonitors _monitors = new();
    private readonly IContentBrowser _content = Substitute.For<IContentBrowser>();
    private readonly IInstalledGameScanner _scanner = Substitute.For<IInstalledGameScanner>();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeCatalog _catalog;
    private readonly PrefetchCoordinator _coordinator;

    public PrefetchCoordinatorTests()
    {
        _catalog = new FakeCatalog(new CatalogDocument
        {
            Games = [Game("cs2", configure: b => b.Steam.Add(730)), Game("apex", configure: b => b.Steam.Add(1172470)), Game("rust", configure: b => b.Steam.Add(252490))],
            Collections =
            [
                new CollectionEntry { Id = "default.matte-black", PackId = "default.matte-black" },
                new CollectionEntry { Id = "default.pastel", PackId = "default.pastel" },
            ],
            Packs =
            [
                new PackEntry { Id = "default.matte-black", Wallpapers = [new WallpaperEntry { Id = "mb-01" }] },
                new PackEntry { Id = "default.pastel", Wallpapers = [new WallpaperEntry { Id = "pa-01" }] },
            ],
        });
        _scanner.Scan().Returns(new List<InstalledGame> { new("steam", "x", null, 730, "CS2"), new("steam", "y", null, 1172470, "Apex") });
        _coordinator = new PrefetchCoordinator(_settings, _catalog, _monitors, _content, _scanner, _time);
    }

    public void Dispose() => _coordinator.Dispose();

    [Fact]
    public void Wants_the_selected_default_collections_and_installed_enabled_games()
    {
        _settings.Update(s => s.GetGame("apex").Enabled = false);

        _coordinator.WantedPacks().ShouldBe(["default.matte-black", "game.cs2"], ignoreOrder: true);
    }

    [Fact]
    public void Individually_selected_wallpapers_pull_in_their_pack()
    {
        _settings.Update(s =>
        {
            s.Default.Selection.Collections = [];
            s.Default.Selection.Wallpapers = ["pa-01"];
        });

        _coordinator.WantedPacks().ShouldContain("default.pastel");
    }

    [Fact]
    public void Game_prefetch_can_be_turned_off()
    {
        _settings.Update(s => s.Content.PrefetchInstalledGames = false);

        _coordinator.WantedPacks().ShouldBe(["default.matte-black"]);
        _scanner.DidNotReceive().Scan();
    }

    [Fact]
    public void A_game_can_stay_enabled_without_background_prefetch()
    {
        _settings.Update(s => s.GetGame("cs2").PrefetchWallpapers = false);
        _coordinator.WantedPacks().ShouldNotContain("game.cs2");
        _coordinator.WantedPacks().ShouldContain("game.apex");
        _settings.Current.IsGameEnabled("cs2").ShouldBeTrue();
        SettingsService.Clone(_settings.Current).Games["cs2"].PrefetchWallpapers.ShouldBeFalse();
    }

    [Fact]
    public void The_user_images_pseudo_collection_is_never_requested()
    {
        _settings.Update(s => s.Default.Selection.Collections = ["user"]);

        _coordinator.WantedPacks().ShouldNotContain("user");
    }

    [Fact]
    public void Run_requests_every_wanted_pack_for_the_current_monitors()
    {
        _coordinator.Run();

        _content.Received().RequestPack("default.matte-black", Arg.Is<IReadOnlyList<MonitorInfo>>(m => m.Count == 1));
        _content.Received().RequestPack("game.cs2", Arg.Any<IReadOnlyList<MonitorInfo>>());
    }

    [Fact]
    public void Nothing_is_requested_without_a_monitor()
    {
        _monitors.Monitors = [];

        _coordinator.Run();

        _content.DidNotReceiveWithAnyArgs().RequestPack(default!, default!);
    }

    [Fact]
    public void Settings_catalog_and_display_changes_retrigger_it()
    {
        _settings.Update(s => s.Default.Selection.Collections = ["default.pastel"]);
        _content.Received().RequestPack("default.pastel", Arg.Any<IReadOnlyList<MonitorInfo>>());

        _content.ClearReceivedCalls();
        _monitors.RaiseChanged();
        _content.Received().RequestPack("default.pastel", Arg.Any<IReadOnlyList<MonitorInfo>>());

        _content.ClearReceivedCalls();
        _catalog.RaiseChanged();
        _content.Received().RequestPack("default.pastel", Arg.Any<IReadOnlyList<MonitorInfo>>());
    }

    [Fact]
    public void The_installed_game_scan_is_cached_for_ten_minutes()
    {
        _coordinator.WantedPacks();
        _coordinator.WantedPacks();
        _scanner.Received(1).Scan();

        _time.Advance(TimeSpan.FromMinutes(11));
        _coordinator.WantedPacks();

        _scanner.Received(2).Scan();
    }
}

public sealed class DetectionConfiguratorTests
{
    [Fact]
    public void Settings_map_to_clamped_timings_and_a_matcher()
    {
        var settings = new AppSettings();
        settings.Detection.PollSeconds = 99;
        settings.Detection.DetectDelaySeconds = 0;
        settings.Detection.ExitGraceSeconds = 1000;
        settings.Detection.Enabled = false;
        var catalog = new CatalogDocument { Games = [Game("g", configure: b => b.Exe.Add("g.exe"))] };

        var config = DetectionConfigurator.Build(settings, catalog, null);

        config.Enabled.ShouldBeFalse();
        config.PollInterval.ShouldBe(TimeSpan.FromSeconds(10));
        config.Options.DetectDelay.ShouldBe(TimeSpan.FromSeconds(1));
        config.Options.ExitGrace.ShouldBe(TimeSpan.FromSeconds(120));
        config.Matcher.Match([Proc(1, "g.exe")], 0).ShouldHaveSingleItem();
    }

    [Fact]
    public void Disabled_games_and_custom_games_flow_through_to_the_matcher()
    {
        var settings = new AppSettings();
        settings.GetGame("g").Enabled = false;
        settings.CustomGames.Add(new CustomGame { Id = "c", DisplayName = "C", ExeNames = ["c.exe"] });
        var catalog = new CatalogDocument { Games = [Game("g", configure: b => b.Exe.Add("g.exe"))] };

        var matcher = DetectionConfigurator.Build(settings, catalog, null).Matcher;

        matcher.Match([Proc(1, "g.exe")], 0).ShouldBeEmpty();
        matcher.Match([Proc(2, "c.exe")], 0).ShouldHaveSingleItem().GameId.ShouldBe("c");
    }

    [Fact]
    public async Task Changes_to_settings_reconfigure_the_running_service()
    {
        var settings = new FakeSettings();
        var catalog = new FakeCatalog(new CatalogDocument { Games = [Game("g", configure: b => b.Exe.Add("g.exe"))] });
        var processes = Substitute.For<IProcessSource>();
        processes.Snapshot().Returns([Proc(1, "g.exe")]);
        var time = new FakeTimeProvider();
        await using var service = new DetectionService(processes, null, null, DetectionConfigurator.Build(settings.Current, catalog.Current, null), time, NullLogger<DetectionService>.Instance);
        using var configurator = new DetectionConfigurator(service, settings, catalog, null);
        DetectionObservation? last = null;
        service.Observed += o => last = o;
        service.EvaluateOnce();
        last!.MatchedGameIds.ShouldBe(["g"]);

        settings.Update(s => s.GetGame("g").Enabled = false);
        service.EvaluateOnce();

        last.MatchedGameIds.ShouldBeEmpty();
    }
}
