using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Tests.Support;
using PrettyDesk.Presentation.ViewModels;
using Shouldly;
using Xunit;

namespace PrettyDesk.Presentation.Tests.ViewModels;

public sealed class HomeViewModelTests : IDisposable
{
    private readonly FakeController _controller = new();
    private readonly FakeMonitorProvider _monitors = new();
    private readonly FakeContentBrowser _content = new();
    private readonly FakeConflicts _conflicts = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private HomeViewModel? _vm;

    public HomeViewModelTests() => Strings.Culture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => _vm?.Dispose();

    private HomeViewModel Create()
    {
        _vm = new HomeViewModel(_controller, _monitors, _content, _conflicts, new InlineDispatcher(), _time);
        AsyncTestWait.UntilAsync(() => !_vm.IsLoadingMonitors).GetAwaiter().GetResult();
        return _vm;
    }

    [Fact]
    public async Task Initial_monitor_enumeration_does_not_block_view_model_construction()
    {
        var monitors = new BlockingMonitorProvider();
        using var vm = new HomeViewModel(_controller, monitors, _content, _conflicts, new InlineDispatcher(), _time);

        vm.IsLoadingMonitors.ShouldBeTrue();
        await monitors.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.IsLoadingMonitors.ShouldBeTrue();

        monitors.Complete([new MonitorInfo("main", 0, 0, 1920, 1080, true)]);
        await AsyncTestWait.UntilAsync(() => !vm.IsLoadingMonitors);

        vm.Monitors.ShouldHaveSingleItem().Id.ShouldBe("main");
    }

    [Fact]
    public async Task Late_monitor_snapshot_is_ignored_after_home_is_disposed()
    {
        var monitors = new BlockingMonitorProvider();
        using var dispatcher = new QueuedDispatcher();
        var vm = new HomeViewModel(_controller, monitors, _content, _conflicts, dispatcher, _time);
        dispatcher.Drain();
        await monitors.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        vm.Dispose();
        monitors.Complete([new MonitorInfo("main", 0, 0, 1920, 1080, true)]);
        await dispatcher.WaitForPostAsync().WaitAsync(TimeSpan.FromSeconds(5));
        dispatcher.Drain();

        vm.Monitors.ShouldBeEmpty();
        vm.IsLoadingMonitors.ShouldBeTrue();
    }

    [Fact]
    public void Stacked_displays_share_their_real_horizontal_origin()
    {
        _monitors.Monitors = [new("upper", 0, -1080, 1920, 1080, false), new("lower", 0, 0, 2560, 1440, true)];
        var vm = Create();
        vm.Monitors[0].X.ShouldBe(vm.Monitors[1].X);
        vm.Monitors[0].Y.ShouldBeLessThan(vm.Monitors[1].Y);
        (vm.Monitors[0].Y + vm.Monitors[0].Height).ShouldBe(vm.Monitors[1].Y, 0.001);
        (vm.Monitors[0].Width / vm.Monitors[1].Width).ShouldBe(1920.0 / 2560, 0.001);
    }

    [Fact]
    public void Shows_the_current_status_text_on_creation()
    {
        _controller.Status = new OrchestratorStatus { Mode = OrchestratorMode.Default, DefaultTitle = "Matte Black", IsRotating = true, NextChange = _time.GetUtcNow() + TimeSpan.FromMinutes(12) };

        var vm = Create();

        vm.Headline.ShouldBe("Default · Matte Black (rotating)");
        vm.Detail.ShouldBe("Next change in 12 min");
    }

    [Fact]
    public void Status_changes_update_headline_and_command_availability()
    {
        var vm = Create();
        vm.NextCommand.CanExecute(null).ShouldBeTrue();

        _controller.Raise(new OrchestratorStatus { Mode = OrchestratorMode.Paused });

        vm.Headline.ShouldBe("Paused");
        vm.CanResume.ShouldBeTrue();
        vm.CanPause.ShouldBeFalse();
        vm.NextCommand.CanExecute(null).ShouldBeFalse();
        vm.ResumeCommand.CanExecute(null).ShouldBeTrue();
        vm.PauseForOneHourCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Buttons_issue_the_matching_controller_commands()
    {
        var vm = Create();

        vm.NextCommand.Execute(null);
        vm.PauseForOneHourCommand.Execute(null);
        vm.PauseUntilResumedCommand.Execute(null);
        _controller.Raise(new OrchestratorStatus { Mode = OrchestratorMode.Paused });
        vm.ResumeCommand.Execute(null);

        _controller.Calls.ShouldBe(["next", "pause:60", "pause:forever", "resume"]);
    }

    [Fact]
    public void Blocked_by_policy_disables_pause_and_next()
    {
        var vm = Create();

        _controller.Raise(new OrchestratorStatus { Mode = OrchestratorMode.Blocked });

        vm.CanPause.ShouldBeFalse();
        vm.CanSkip.ShouldBeFalse();
        vm.Headline.ShouldBe("Your organization manages your wallpaper");
    }

    [Fact]
    public void While_loading_a_game_pack_next_is_unavailable()
    {
        var vm = Create();

        _controller.Raise(new OrchestratorStatus { Mode = OrchestratorMode.GameLoading, GameName = "X" });

        vm.CanSkip.ShouldBeFalse();
    }

    [Fact]
    public void Single_monitor_fills_the_canvas_and_keeps_its_aspect_ratio()
    {
        var vm = Create();

        var monitor = vm.Monitors.ShouldHaveSingleItem();
        monitor.Width.ShouldBe(HomeViewModel.CanvasWidth, 0.001);
        (monitor.Width / monitor.Height).ShouldBe(16.0 / 9, 0.001);
        monitor.Label.ShouldBe("Display 1 · 1920×1080 (main)");
        vm.HasMonitors.ShouldBeTrue();
    }

    [Fact]
    public void Two_monitors_are_laid_out_side_by_side_to_scale_without_overlap()
    {
        _monitors.Monitors = [new MonitorInfo("a", 0, 0, 2560, 1440, true), new MonitorInfo("b", 2560, 0, 1080, 1920, false)];

        var vm = Create();

        vm.Monitors.Count.ShouldBe(2);
        var (left, right) = (vm.Monitors[0], vm.Monitors[1]);
        (left.X + left.Width).ShouldBe(right.X, 0.001);
        (right.X + right.Width).ShouldBeLessThanOrEqualTo(HomeViewModel.CanvasWidth + 0.001);
        (left.Height / right.Height).ShouldBe(1440.0 / 1920, 0.001);
        vm.CanvasHeight.ShouldBeGreaterThanOrEqualTo(right.Y + right.Height);
    }

    [Fact]
    public void Monitors_with_negative_coordinates_are_normalised()
    {
        _monitors.Monitors = [new MonitorInfo("left", -1920, 0, 1920, 1080, false), new MonitorInfo("main", 0, 0, 1920, 1080, true)];

        var vm = Create();

        vm.Monitors.Min(m => m.X).ShouldBeGreaterThanOrEqualTo(0);
        vm.Monitors[0].Id.ShouldBe("left");
    }

    [Fact]
    public void No_monitors_shows_the_designed_empty_state()
    {
        _monitors.Monitors = [];

        var vm = Create();

        vm.Monitors.ShouldBeEmpty();
        vm.HasMonitors.ShouldBeFalse();
    }

    [Fact]
    public async Task Display_changes_rebuild_the_layout()
    {
        var vm = Create();
        _monitors.Monitors.Add(new MonitorInfo("m2", 1920, 0, 1920, 1080, false));

        _monitors.Raise();

        await AsyncTestWait.UntilAsync(() => vm.Monitors.Count == 2);
        vm.Monitors.Count.ShouldBe(2);
    }

    [Fact]
    public void Thumbnails_follow_the_current_wallpaper_of_each_monitor()
    {
        _content.Previews["mb-01"] = "/thumbs/mb-01.jpg";
        _content.Previews["mb-02"] = "/thumbs/mb-02.jpg";
        _controller.Status = new OrchestratorStatus { WallpaperByMonitor = new Dictionary<string, string> { ["m1"] = "mb-01" } };
        var vm = Create();
        vm.Monitors[0].ThumbnailPath.ShouldBe("/thumbs/mb-01.jpg");

        _controller.Raise(new OrchestratorStatus { WallpaperByMonitor = new Dictionary<string, string> { ["m1"] = "mb-02" } });

        vm.Monitors[0].ThumbnailPath.ShouldBe("/thumbs/mb-02.jpg");
        vm.Monitors[0].HasThumbnail.ShouldBeTrue();
    }

    [Fact]
    public void A_monitor_without_a_wallpaper_has_no_thumbnail_yet()
    {
        Create().Monitors[0].HasThumbnail.ShouldBeFalse();
    }

    [Fact]
    public void Thumbnails_appear_when_content_arrives()
    {
        _controller.Status = new OrchestratorStatus { WallpaperByMonitor = new Dictionary<string, string> { ["m1"] = "x" } };
        var vm = Create();
        vm.Monitors[0].HasThumbnail.ShouldBeFalse();

        _content.Previews["x"] = "/t.jpg";
        _content.RaisePackChanged("p");

        vm.Monitors[0].ThumbnailPath.ShouldBe("/t.jpg");
    }

    [Theory]
    [InlineData(EnvironmentConflict.PolicyLocked, BannerSeverity.Error)]
    [InlineData(EnvironmentConflict.WallpaperEngine, BannerSeverity.Warning)]
    [InlineData(EnvironmentConflict.Lively, BannerSeverity.Warning)]
    [InlineData(EnvironmentConflict.SpotlightOrSlideshow, BannerSeverity.Info)]
    public void Each_environment_conflict_gets_an_explanatory_banner(EnvironmentConflict conflict, BannerSeverity severity)
    {
        _conflicts.Value = conflict;

        var banner = Create().Banners.ShouldHaveSingleItem();

        banner.Kind.ShouldBe(conflict);
        banner.Severity.ShouldBe(severity);
        banner.Title.ShouldNotBeNullOrWhiteSpace();
        banner.Message.Length.ShouldBeGreaterThan(30);
    }

    [Fact]
    public void Several_conflicts_produce_several_banners_and_none_means_none()
    {
        _conflicts.Value = EnvironmentConflict.WallpaperEngine | EnvironmentConflict.SpotlightOrSlideshow;
        var vm = Create();
        vm.Banners.Count.ShouldBe(2);

        _conflicts.Value = EnvironmentConflict.None;
        vm.Refresh();

        vm.Banners.ShouldBeEmpty();
    }

    [Fact]
    public void A_banner_can_be_dismissed()
    {
        _conflicts.Value = EnvironmentConflict.Lively;
        var vm = Create();

        vm.DismissBannerCommand.Execute(vm.Banners[0]);

        vm.Banners.ShouldBeEmpty();
    }

    [Fact]
    public void Disposing_stops_listening_to_the_controller()
    {
        var vm = Create();
        vm.Dispose();

        _controller.Raise(new OrchestratorStatus { Mode = OrchestratorMode.Paused });

        vm.Headline.ShouldNotBe("Paused");
    }
}
