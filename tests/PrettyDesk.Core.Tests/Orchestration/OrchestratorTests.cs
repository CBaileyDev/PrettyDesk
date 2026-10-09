using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Core.Rotation;
using PrettyDesk.Core.Settings;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Orchestration;

public sealed class OrchestratorTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeMonitors _monitors = new();
    private readonly FakeSetter _setter = new();
    private readonly FakeContent _content = new();
    private readonly FakeSystem _system = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeCatalog _catalog;
    private readonly Dictionary<string, ContextRotationState> _rotation = [];
    private readonly WallpaperOrchestrator _orchestrator;

    public OrchestratorTests()
    {
        _catalog = new FakeCatalog(new CatalogDocument
        {
            Games = [TestData.Game("cs2", configure: b => b.Exe.Add("cs2.exe")), TestData.Game("valorant", configure: b => b.Exe.Add("v.exe")), TestData.Game("nothing", configure: b => b.Exe.Add("n.exe"))],
            Collections =
            [
                new CollectionEntry { Id = "default.matte-black", PackId = "default.matte-black", Title = "Matte Black" },
                new CollectionEntry { Id = "default.clean-white", PackId = "default.clean-white", Title = "Clean White", Tone = Tones.Light },
            ],
        });
        _content.AddPack("default.matte-black", Tones.Dark, true, "mb-01", "mb-02", "mb-03");
        _content.AddPack("default.clean-white", Tones.Light, true, "cw-01", "cw-02");
        _content.AddPack("game.cs2", Tones.Dark, true, "cs2-hero", "cs2-min", "cs2-mood");
        _content.AddPack("game.valorant", Tones.Dark, false, "val-hero", "val-min");
        _content.AddPack("game.nothing", Tones.Dark, true);

        var scheduler = new RotationScheduler(_rotation, _time, new Random(1));
        _orchestrator = new WallpaperOrchestrator(_monitors, _setter, new FakeRenderer(), _content, _system, _catalog, _settings, scheduler, null, _time, NullLogger<WallpaperOrchestrator>.Instance);
    }

    public ValueTask DisposeAsync() => _orchestrator.DisposeAsync();

    private Task Reconcile() => _orchestrator.ReconcileAsync(TestContext.Current.CancellationToken);

    private string LastPath(string monitor = "M1") => _setter.Calls.Last(c => c.Monitor == monitor).Path;

    // ---- Default / Game transitions (§5.4) -------------------------------------------------------------------

    [Fact]
    public async Task Disposing_twice_is_harmless()
    {
        _orchestrator.Start();
        await _orchestrator.DisposeAsync();

        await Should.NotThrowAsync(async () => await _orchestrator.DisposeAsync());
    }

    [Fact]
    public async Task Applies_a_default_wallpaper_at_start()
    {
        await Reconcile();

        _setter.Calls.ShouldHaveSingleItem().Path.ShouldStartWith("/cache/mb-0");
        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Default);
        _orchestrator.Status.DefaultTitle.ShouldBe("Matte Black");
        _orchestrator.Status.IsRotating.ShouldBeTrue();
    }

    // ---- Only dark wallpapers ---------------------------------------------------------------------------------

    [Fact]
    public async Task Dark_only_with_only_light_collections_selected_shows_nothing_and_says_so()
    {
        _settings.Update(s =>
        {
            s.General.DarkWallpapersOnly = true;
            s.Default.Selection.Collections = ["default.clean-white"];
        });

        await Reconcile();

        _setter.Calls.ShouldBeEmpty("a light-only selection must not be shown while dark-only is on");
        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Default);
        _orchestrator.Status.NoContent.ShouldBeTrue();
    }

    [Fact]
    public async Task Dark_only_rotates_through_dark_default_wallpapers()
    {
        _settings.Update(s => s.General.DarkWallpapersOnly = true);

        await Reconcile();

        _setter.Calls.ShouldHaveSingleItem().Path.ShouldStartWith("/cache/mb-0");
        _orchestrator.Status.NoContent.ShouldBeFalse();
    }

    [Fact]
    public async Task Dark_only_keeps_light_game_wallpapers_out_of_the_game_rotation()
    {
        _content.Packs["game.cs2"].Add("cs2-light");
        _content.MakeAvailable("game.cs2", "cs2-light", Tones.Light);
        _settings.Update(s => s.General.DarkWallpapersOnly = true);

        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        _time.Advance(TimeSpan.FromSeconds(5));
        await Reconcile();

        _orchestrator.Status.GameId.ShouldBe("cs2");
        _setter.Calls.ShouldNotBeEmpty();
        _setter.Calls.Where(c => c.Path.Contains("cs2-light", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_monitor_gets_a_render_at_its_own_pixel_size()
    {
        _monitors.Monitors = [FakeMonitors.Mon("M1", 3840, 2160, true), FakeMonitors.Mon("M2", 1080, 1920)];

        await Reconcile();

        LastPath("M1").ShouldEndWith("_3840x2160.png");
        LastPath("M2").ShouldEndWith("_1080x1920.png");
        _orchestrator.Status.WallpaperByMonitor["M1"].ShouldBe(_orchestrator.Status.WallpaperByMonitor["M2"]);
    }

    [Fact]
    public async Task Game_start_switches_to_the_game_pack_and_exit_returns_to_the_same_default_wallpaper()
    {
        await Reconcile();
        var before = LastPath();

        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        _time.Advance(TimeSpan.FromSeconds(5));
        await Reconcile();
        LastPath().ShouldStartWith("/cache/cs2-");
        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Game);
        _orchestrator.Status.GameName.ShouldBe("cs2");

        _orchestrator.SetActiveGame(null);
        _time.Advance(TimeSpan.FromSeconds(5));
        await Reconcile();

        LastPath().ShouldBe(before);
        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Default);
    }

    [Fact]
    public async Task Entering_a_game_does_not_advance_the_default_rotation()
    {
        await Reconcile();
        var shown = _rotation["default"].CurrentWallpaperId;

        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        _time.Advance(TimeSpan.FromMinutes(10));
        await Reconcile();
        _orchestrator.SetActiveGame(null);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Reconcile();

        _rotation["default"].CurrentWallpaperId.ShouldBe(shown);
    }

    [Fact]
    public async Task Default_advances_once_after_a_long_game_session()
    {
        await Reconcile();
        var shown = _rotation["default"].CurrentWallpaperId;

        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        _time.Advance(TimeSpan.FromHours(3));
        await Reconcile();
        _orchestrator.SetActiveGame(null);
        await Reconcile();

        _rotation["default"].CurrentWallpaperId.ShouldNotBe(shown);
    }

    [Fact]
    public async Task Grace_period_keeps_the_game_wallpaper_and_reports_grace()
    {
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        await Reconcile();
        var gameWallpaper = LastPath();

        _orchestrator.SetActiveGame(new ActiveGame("cs2", InGrace: true));
        _time.Advance(TimeSpan.FromSeconds(3));
        await Reconcile();

        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.GameGrace);
        _setter.Calls.Last().Path.ShouldBe(gameWallpaper);
    }

    [Fact]
    public async Task Game_wallpaper_stays_through_the_session_with_the_default_per_session_interval()
    {
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        await Reconcile();
        var first = LastPath();

        _time.Advance(TimeSpan.FromHours(6));
        await Reconcile();

        LastPath().ShouldBe(first);
        _setter.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Next_session_shows_the_next_game_wallpaper()
    {
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        await Reconcile();
        var first = LastPath();
        _orchestrator.SetActiveGame(null);
        _time.Advance(TimeSpan.FromSeconds(5));
        await Reconcile();

        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        _time.Advance(TimeSpan.FromSeconds(5));
        await Reconcile();

        LastPath().ShouldNotBe(first);
        LastPath().ShouldStartWith("/cache/cs2-");
    }

    [Fact]
    public async Task Game_with_content_not_downloaded_keeps_the_current_wallpaper_and_requests_the_pack()
    {
        await Reconcile();
        var calls = _setter.Calls.Count;

        _orchestrator.SetActiveGame(new ActiveGame("valorant", false));
        _time.Advance(TimeSpan.FromSeconds(5));
        await Reconcile();

        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.GameLoading);
        _orchestrator.Status.GameName.ShouldBe("valorant");
        _setter.Calls.Count.ShouldBe(calls);
        _content.Requested.ShouldContain("game.valorant");
    }

    [Fact]
    public async Task Game_whose_pack_has_no_wallpapers_falls_back_to_default_instead_of_loading_forever()
    {
        await Reconcile();
        var before = LastPath();

        _orchestrator.SetActiveGame(new ActiveGame("nothing", false));
        _time.Advance(TimeSpan.FromSeconds(5));
        await Reconcile();

        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Default);
        LastPath().ShouldBe(before);
        _content.Requested.ShouldNotContain("game.nothing");
    }

    [Fact]
    public async Task Game_with_every_wallpaper_excluded_also_falls_back_to_default()
    {
        _settings.Update(s => s.GetGame("valorant").Excluded = ["val-hero", "val-min"]);

        _orchestrator.SetActiveGame(new ActiveGame("valorant", false));
        _time.Advance(TimeSpan.FromSeconds(5));
        await Reconcile();

        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Default);
        _content.Requested.ShouldNotContain("game.valorant");
    }

    [Fact]
    public async Task Game_wallpaper_is_applied_as_soon_as_the_pack_arrives_if_still_active()
    {
        _orchestrator.SetActiveGame(new ActiveGame("valorant", false));
        await Reconcile();

        _content.Download("game.valorant");
        _time.Advance(TimeSpan.FromSeconds(5));
        await Reconcile();

        LastPath().ShouldStartWith("/cache/val-");
        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Game);
    }

    [Fact]
    public async Task Disabling_a_game_mid_session_returns_to_default()
    {
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        await Reconcile();

        _settings.Update(s => s.GetGame("cs2").Enabled = false);
        _time.Advance(TimeSpan.FromSeconds(5));
        await Reconcile();

        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Default);
        LastPath().ShouldStartWith("/cache/mb-");
    }

    [Fact]
    public async Task Fixed_game_wallpaper_is_used_when_the_user_picked_a_favourite()
    {
        _settings.Update(s =>
        {
            var g = s.GetGame("cs2");
            g.Mode = WallpaperMode.Fixed;
            g.FixedWallpaperId = "cs2-min";
        });
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));

        await Reconcile();

        LastPath().ShouldStartWith("/cache/cs2-min");
        _orchestrator.Status.IsRotating.ShouldBeFalse();
    }

    [Fact]
    public async Task Excluded_game_wallpapers_are_never_shown()
    {
        _settings.Update(s => s.GetGame("cs2").Excluded = ["cs2-hero", "cs2-mood"]);
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));

        await Reconcile();

        LastPath().ShouldStartWith("/cache/cs2-min");
    }

    // ---- diff-based applying (FR-APPLY-4) --------------------------------------------------------------------

    [Fact]
    public async Task Reconciling_again_with_no_changes_applies_nothing()
    {
        await Reconcile();
        _time.Advance(TimeSpan.FromSeconds(10));
        await Reconcile();
        await Reconcile();

        _setter.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Skips_apply_when_windows_already_shows_the_target_path()
    {
        _setter.Current["M1"] = "/cache/mb-01_1920x1080.png";
        _setter.Current["M1"] = null;
        // Pre-seed whichever wallpaper the scheduler will choose.
        var scheduler = new RotationScheduler(new Dictionary<string, ContextRotationState>(), _time, new Random(1));
        var expected = scheduler.Resolve("default", ["mb-01", "mb-02", "mb-03"], new RotationPolicy(RotationOrder.Shuffle, RotationInterval.Every(TimeSpan.FromMinutes(30)))).WallpaperId;
        _setter.Current["M1"] = $"/cache/{expected}_1920x1080.png";

        await Reconcile();

        _setter.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bursts_are_coalesced_to_one_apply_per_monitor_per_two_seconds()
    {
        _orchestrator.NextWallpaper();
        await Reconcile();
        _orchestrator.NextWallpaper();
        await Reconcile();

        _setter.Calls.Count.ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(2));
        await Reconcile();
        _setter.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Desktop_reset_forces_a_reapply()
    {
        await Reconcile();
        _time.Advance(TimeSpan.FromSeconds(3));

        _orchestrator.NotifyDesktopReset();
        await Reconcile();

        _setter.Calls.Count.ShouldBe(2);
        _setter.Calls[1].Path.ShouldBe(_setter.Calls[0].Path);
    }

    // ---- pause (FR-WP-7) -------------------------------------------------------------------------------------

    [Fact]
    public async Task Pause_freezes_the_desktop_even_when_a_game_starts()
    {
        await Reconcile();
        var calls = _setter.Calls.Count;

        _orchestrator.Pause(null);
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        _time.Advance(TimeSpan.FromHours(5));
        await Reconcile();

        _setter.Calls.Count.ShouldBe(calls);
        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Paused);
    }

    [Fact]
    public async Task Resume_applies_the_pending_game_context()
    {
        _orchestrator.Pause(null);
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        await Reconcile();

        _orchestrator.Resume();
        await Reconcile();

        LastPath().ShouldStartWith("/cache/cs2-");
    }

    [Fact]
    public async Task Timed_pause_expires_on_its_own()
    {
        await Reconcile();
        _orchestrator.Pause(TimeSpan.FromHours(1));
        await Reconcile();
        _orchestrator.Status.PausedUntil.ShouldBe(_time.GetUtcNow() + TimeSpan.FromHours(1));

        _time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
        await Reconcile();

        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Default);
        _settings.Current.General.Paused.ShouldBeFalse();
    }

    // ---- policy / environment --------------------------------------------------------------------------------

    [Fact]
    public async Task Policy_locked_wallpaper_blocks_applying()
    {
        _system.IsWallpaperPolicyLocked = true;

        await Reconcile();

        _setter.Calls.ShouldBeEmpty();
        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Blocked);
    }

    [Fact]
    public async Task Failed_apply_is_reported_then_retried_with_backoff()
    {
        _setter.Fail = true;
        await Reconcile();
        _orchestrator.Status.ApplyFailed.ShouldBeTrue();

        _setter.Fail = false;
        _time.Advance(TimeSpan.FromSeconds(3));
        await Reconcile();

        _orchestrator.Status.ApplyFailed.ShouldBeFalse();
        _setter.Calls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task No_content_at_all_is_reported_instead_of_crashing()
    {
        _content.Assets.Clear();

        await Reconcile();

        _orchestrator.Status.NoContent.ShouldBeTrue();
        _setter.Calls.ShouldBeEmpty();
    }

    // ---- monitors --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Monitor_plugged_in_mid_game_receives_the_game_wallpaper()
    {
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        await Reconcile();
        _monitors.Monitors.Add(FakeMonitors.Mon("M2", 2560, 1440));
        _time.Advance(TimeSpan.FromSeconds(3));

        await Reconcile();

        LastPath("M2").ShouldStartWith("/cache/cs2-");
        LastPath("M2").ShouldEndWith("_2560x1440.png");
        _setter.Calls.Count(c => c.Monitor == "M1").ShouldBe(1);
    }

    [Fact]
    public async Task Monitor_unplugged_is_forgotten_and_reapplied_fresh_when_it_returns()
    {
        _monitors.Monitors = [FakeMonitors.Mon("M1", 1920, 1080, true), FakeMonitors.Mon("M2", 1920, 1080)];
        await Reconcile();
        _monitors.Monitors.RemoveAt(1);
        _time.Advance(TimeSpan.FromSeconds(3));
        await Reconcile();
        _setter.Current["M2"] = null;

        _monitors.Monitors.Add(FakeMonitors.Mon("M2", 1920, 1080));
        _time.Advance(TimeSpan.FromSeconds(3));
        await Reconcile();

        _setter.Calls.Count(c => c.Monitor == "M2").ShouldBe(2);
    }

    [Fact]
    public async Task Different_mode_gives_each_monitor_its_own_wallpaper()
    {
        _settings.Update(s => s.Monitors.Mode = MonitorMode.Different);
        _monitors.Monitors = [FakeMonitors.Mon("M1", 1920, 1080, true), FakeMonitors.Mon("M2", 1920, 1080)];

        await Reconcile();

        _orchestrator.Status.WallpaperByMonitor["M1"].ShouldNotBe(_orchestrator.Status.WallpaperByMonitor["M2"]);
    }

    [Fact]
    public async Task Game_on_secondary_only_leaves_the_primary_monitor_on_the_default()
    {
        _settings.Update(s => s.Monitors.GameOnSecondaryOnly = true);
        _monitors.Monitors = [FakeMonitors.Mon("M1", 1920, 1080, true), FakeMonitors.Mon("M2", 1920, 1080)];
        await Reconcile();
        var primaryBefore = LastPath("M1");

        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        _time.Advance(TimeSpan.FromSeconds(3));
        await Reconcile();

        LastPath("M1").ShouldBe(primaryBefore);
        LastPath("M2").ShouldStartWith("/cache/cs2-");
    }

    [Fact]
    public async Task Game_on_secondary_only_with_a_single_monitor_still_changes_it()
    {
        _settings.Update(s => s.Monitors.GameOnSecondaryOnly = true);
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));

        await Reconcile();

        LastPath().ShouldStartWith("/cache/cs2-");
    }

    // ---- default selection -----------------------------------------------------------------------------------

    [Fact]
    public async Task Fixed_default_wallpaper_never_rotates()
    {
        _settings.Update(s =>
        {
            s.Default.Mode = WallpaperMode.Fixed;
            s.Default.FixedWallpaperId = "mb-02";
        });

        await Reconcile();
        _time.Advance(TimeSpan.FromDays(2));
        await Reconcile();

        _setter.Calls.ShouldHaveSingleItem().Path.ShouldStartWith("/cache/mb-02");
        _orchestrator.Status.IsRotating.ShouldBeFalse();
    }

    [Fact]
    public async Task Selection_can_mix_collections_individual_wallpapers_and_exclusions()
    {
        _settings.Update(s =>
        {
            s.Default.Selection.Collections = ["default.clean-white"];
            s.Default.Selection.Wallpapers = ["mb-03"];
            s.Default.Selection.Excluded = ["cw-02"];
        });

        var plan = ContextPlanner.PlanDefault(_settings.Current, _catalog.Current, _content, _system);

        plan.Pool.ShouldBe(["cw-01", "mb-03"], ignoreOrder: true);
    }

    [Fact]
    public async Task Follow_windows_theme_restricts_the_pool_by_tone_with_fallback()
    {
        _settings.Update(s =>
        {
            s.Default.Selection.Collections = ["default.matte-black", "default.clean-white"];
            s.Default.FollowWindowsTheme = true;
        });

        _system.IsLightTheme = true;
        ContextPlanner.PlanDefault(_settings.Current, _catalog.Current, _content, _system).Pool.ShouldBe(["cw-01", "cw-02"]);

        _system.IsLightTheme = false;
        ContextPlanner.PlanDefault(_settings.Current, _catalog.Current, _content, _system).Pool.ShouldBe(["mb-01", "mb-02", "mb-03"]);

        _settings.Update(s => s.Default.Selection.Collections = ["default.matte-black"]);
        _system.IsLightTheme = true;
        ContextPlanner.PlanDefault(_settings.Current, _catalog.Current, _content, _system).Pool.Count.ShouldBe(3);
        await Task.CompletedTask;
    }

    [Fact]
    public void Battery_saver_pauses_default_rotation_only_when_enabled()
    {
        _system.IsBatterySaverOn = true;
        ContextPlanner.PlanDefault(_settings.Current, _catalog.Current, _content, _system).Policy.AutoRotationPaused.ShouldBeTrue();

        _settings.Update(s => s.Default.PauseRotationOnBatterySaver = false);
        ContextPlanner.PlanDefault(_settings.Current, _catalog.Current, _content, _system).Policy.AutoRotationPaused.ShouldBeFalse();
    }

    [Fact]
    public void Battery_saver_never_stops_game_context_switching()
    {
        _system.IsBatterySaverOn = true;
        var game = new GameDefinition("cs2", "cs2", new DetectionRules(), "game.cs2", false);

        ContextPlanner.PlanGame(game, _settings.Current, _catalog.Current, _content).Policy.AutoRotationPaused.ShouldBeFalse();
    }

    [Fact]
    public void Custom_game_uses_its_own_wallpaper_list()
    {
        _content.MakeAvailable("user", "user:a.png");
        _settings.Update(s => s.CustomGames.Add(new CustomGame { Id = "custom-1", DisplayName = "Mine", ExeNames = ["m.exe"], Wallpapers = ["user:a.png", "mb-01", "not-downloaded"] }));
        var game = GameRegistry.Build(_catalog.Current, _settings.Current).Find("custom-1")!;

        ContextPlanner.PlanGame(game, _settings.Current, _catalog.Current, _content).Pool.ShouldBe(["user:a.png", "mb-01"]);
    }

    // ---- manual controls / preview ---------------------------------------------------------------------------

    [Fact]
    public async Task Next_wallpaper_advances_the_current_context()
    {
        await Reconcile();
        var first = _rotation["default"].CurrentWallpaperId;
        _time.Advance(TimeSpan.FromSeconds(3));

        _orchestrator.NextWallpaper();
        await Reconcile();

        _rotation["default"].CurrentWallpaperId.ShouldNotBe(first);
        _setter.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Next_wallpaper_during_a_game_advances_the_game_context_not_the_default()
    {
        await Reconcile();
        var defaultShown = _rotation["default"].CurrentWallpaperId;
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        _time.Advance(TimeSpan.FromSeconds(3));
        await Reconcile();
        var gameShown = _rotation["game:cs2"].CurrentWallpaperId;
        _time.Advance(TimeSpan.FromSeconds(3));

        _orchestrator.NextWallpaper();
        await Reconcile();

        _rotation["game:cs2"].CurrentWallpaperId.ShouldNotBe(gameShown);
        _rotation["default"].CurrentWallpaperId.ShouldBe(defaultShown);
    }

    [Fact]
    public async Task Preview_shows_a_wallpaper_temporarily_then_restores()
    {
        await Reconcile();
        var original = LastPath();
        _time.Advance(TimeSpan.FromSeconds(3));

        _orchestrator.Preview("cs2-hero");
        await Reconcile();
        LastPath().ShouldStartWith("/cache/cs2-hero");
        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Preview);

        _time.Advance(WallpaperOrchestrator.DefaultPreviewDuration + TimeSpan.FromSeconds(1));
        await Reconcile();

        LastPath().ShouldBe(original);
        _orchestrator.Status.Mode.ShouldBe(OrchestratorMode.Default);
    }

    [Fact]
    public async Task Cancelling_a_preview_restores_immediately()
    {
        await Reconcile();
        var original = LastPath();
        _time.Advance(TimeSpan.FromSeconds(3));
        _orchestrator.Preview("cs2-hero");
        await Reconcile();
        _time.Advance(TimeSpan.FromSeconds(3));

        _orchestrator.CancelPreview();
        await Reconcile();

        LastPath().ShouldBe(original);
    }

    [Fact]
    public async Task Unlock_interval_rotates_the_default_on_unlock()
    {
        _settings.Update(s => s.Default.Interval = RotationInterval.Unlock);
        await Reconcile();
        var first = _rotation["default"].CurrentWallpaperId;

        _orchestrator.NotifyUnlock();
        _time.Advance(TimeSpan.FromSeconds(3));
        await Reconcile();

        _rotation["default"].CurrentWallpaperId.ShouldNotBe(first);
    }

    // ---- status / events -------------------------------------------------------------------------------------

    [Fact]
    public async Task Status_reports_next_change_for_rotating_defaults()
    {
        await Reconcile();

        _orchestrator.Status.NextChange.ShouldBe(_time.GetUtcNow() + TimeSpan.FromMinutes(30));
        _orchestrator.Status.PoolSize.ShouldBe(3);
    }

    [Fact]
    public async Task Status_event_fires_only_when_something_changed()
    {
        var count = 0;
        _orchestrator.StatusChanged += _ => count++;

        await Reconcile();
        await Reconcile();
        await Reconcile();

        count.ShouldBe(1);
    }

    // ---- the background loop ---------------------------------------------------------------------------------

    [Fact]
    public async Task Started_orchestrator_applies_on_its_own_and_reacts_to_game_changes()
    {
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _orchestrator.StatusChanged += s =>
        {
            if (s.Mode == OrchestratorMode.Game)
            {
                applied.TrySetResult();
            }
        };

        _orchestrator.Start();
        _orchestrator.SetActiveGame(new ActiveGame("cs2", false));
        for (var i = 0; i < 40 && !applied.Task.IsCompleted; i++)
        {
            await Task.Yield();
            _time.Advance(TimeSpan.FromMilliseconds(300));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        await applied.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        _setter.Calls.Last().Path.ShouldStartWith("/cache/cs2-");
    }
}

public sealed class OrchestratorBackupTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeSetter _setter = new();
    private readonly RecordingBackup _backup;
    private readonly WallpaperOrchestrator _orchestrator;

    public OrchestratorBackupTests()
    {
        _backup = new RecordingBackup(() => _setter.Calls.Count);
        var content = new FakeContent();
        content.AddPack("default.matte-black", Tones.Dark, true, "mb-01", "mb-02");
        var catalog = new FakeCatalog(new CatalogDocument
        {
            Collections = [new CollectionEntry { Id = "default.matte-black", PackId = "default.matte-black", Title = "Matte Black" }],
        });
        _orchestrator = new WallpaperOrchestrator(
            new FakeMonitors(), _setter, new FakeRenderer(), content, new FakeSystem(), catalog, new FakeSettings(),
            new RotationScheduler(new Dictionary<string, ContextRotationState>(), _time, new Random(1)), null, _time,
            NullLogger<WallpaperOrchestrator>.Instance, _backup);
    }

    public ValueTask DisposeAsync() => _orchestrator.DisposeAsync();

    [Fact]
    public async Task Original_wallpaper_is_backed_up_before_the_very_first_apply()
    {
        await _orchestrator.ReconcileAsync(TestContext.Current.CancellationToken);

        _backup.Calls.ShouldBe(1);
        _backup.AppliedCountAtBackup.ShouldBe(0);
        _setter.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Backup_is_requested_only_until_it_succeeds()
    {
        await _orchestrator.ReconcileAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(5));
        await _orchestrator.ReconcileAsync(TestContext.Current.CancellationToken);

        _backup.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_backup_prevents_overwriting_the_users_wallpaper_and_retries()
    {
        _backup.Fail = true;

        await _orchestrator.ReconcileAsync(TestContext.Current.CancellationToken);

        _setter.Calls.ShouldBeEmpty();
        _orchestrator.Status.ApplyFailed.ShouldBeTrue();

        _backup.Fail = false;
        _time.Advance(TimeSpan.FromSeconds(5));
        await _orchestrator.ReconcileAsync(TestContext.Current.CancellationToken);

        _setter.Calls.Count.ShouldBe(1);
        _backup.Calls.ShouldBe(2);
    }

    private sealed class RecordingBackup(Func<int> appliedCount) : IWallpaperBackup
    {
        public int Calls { get; private set; }
        public int AppliedCountAtBackup { get; private set; } = -1;
        public bool Fail { get; set; }

        public Task EnsureBackupAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            AppliedCountAtBackup = appliedCount();
            return Fail ? throw new IOException("disk full") : Task.CompletedTask;
        }
    }
}

public sealed class OrchestratorPreviewBackupTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeSetter _setter = new();
    private readonly CountingBackup _backup = new();
    private readonly WallpaperOrchestrator _orchestrator;

    public OrchestratorPreviewBackupTests()
    {
        var content = new FakeContent();
        content.AddPack("default.matte-black", Tones.Dark, true, "mb-01", "mb-02");
        var catalog = new FakeCatalog(new CatalogDocument
        {
            Collections = [new CollectionEntry { Id = "default.matte-black", PackId = "default.matte-black", Title = "Matte Black" }],
        });
        _orchestrator = new WallpaperOrchestrator(
            new FakeMonitors(), _setter, new FakeRenderer(), content, new FakeSystem(), catalog, new FakeSettings(),
            new RotationScheduler(new Dictionary<string, ContextRotationState>(), _time, new Random(1)), null, _time,
            NullLogger<WallpaperOrchestrator>.Instance, _backup);
    }

    public ValueTask DisposeAsync() => _orchestrator.DisposeAsync();

    [Fact]
    public async Task A_preview_as_the_very_first_apply_backs_up_the_original_first()
    {
        _orchestrator.Preview("mb-02");

        await _orchestrator.ReconcileAsync(TestContext.Current.CancellationToken);

        _backup.Calls.ShouldBe(1);
        _backup.AppliedCountAtBackup.ShouldBe(0);
        _setter.Calls.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_preview_never_overwrites_the_original_when_the_backup_fails()
    {
        _backup.Fail = true;
        _orchestrator.Preview("mb-02");

        await _orchestrator.ReconcileAsync(TestContext.Current.CancellationToken);

        _setter.Calls.ShouldBeEmpty();
        _orchestrator.Status.ApplyFailed.ShouldBeTrue();
    }

    private sealed class CountingBackup : IWallpaperBackup
    {
        public int Calls { get; private set; }
        public int AppliedCountAtBackup { get; private set; } = -1;
        public bool Fail { get; set; }

        public Task EnsureBackupAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            AppliedCountAtBackup = 0;
            return Fail ? throw new IOException("disk full") : Task.CompletedTask;
        }
    }
}
