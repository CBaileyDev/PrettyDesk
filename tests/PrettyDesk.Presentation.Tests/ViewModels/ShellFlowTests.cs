using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Presentation.Tests.Support;
using PrettyDesk.Presentation.ViewModels;
using Shouldly;
using Xunit;

namespace PrettyDesk.Presentation.Tests.ViewModels;

public class OnboardingViewModelTests
{
    private readonly FakeSettingsProvider _settings = new();
    private readonly FakeCatalogProvider _catalog;
    private readonly FakeInstalled _installed = new();
    private readonly IStartupService _startup = Substitute.For<IStartupService>();
    private readonly FakeConflicts _conflicts = new();
    private readonly FakeContentBrowser _content = new();
    private readonly FakeMonitorProvider _monitors = new();

    public OnboardingViewModelTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en-US");
        _catalog = new FakeCatalogProvider(new CatalogDocument
        {
            Games =
            [
                new GameEntry { Id = "cs2", DisplayName = "Counter-Strike 2", PackId = "game.cs2" },
                new GameEntry { Id = "apex", DisplayName = "Apex Legends", PackId = "game.apex" },
                new GameEntry { Id = "rust", DisplayName = "Rust", PackId = "game.rust" },
            ],
            Collections =
            [
                new CollectionEntry { Id = "default.matte-black", PackId = "default.matte-black", Order = 1 },
                new CollectionEntry { Id = "default.deep-space", PackId = "default.deep-space", Order = 2 },
                new CollectionEntry { Id = "default.steel-blue-night", PackId = "default.steel-blue-night", Order = 3 },
                new CollectionEntry { Id = "default.architectural", PackId = "default.architectural", Order = 4 },
            ],
            Packs =
            [
                new PackEntry { Id = "default.matte-black", Wallpapers = [new WallpaperEntry { Id = "mb-01" }, new WallpaperEntry { Id = "mb-02", Starter = true }] },
                new PackEntry { Id = "default.deep-space", Wallpapers = [new WallpaperEntry { Id = "ds-01", Starter = true }] },
                new PackEntry { Id = "default.steel-blue-night", Wallpapers = [] },
                new PackEntry { Id = "default.architectural", Wallpapers = [new WallpaperEntry { Id = "ar-01", Tone = Tones.Light }, new WallpaperEntry { Id = "ar-02", Tone = Tones.Dark }] },
            ],
        });
    }

    private OnboardingViewModel Vm() => new(_settings, _catalog, _installed, _startup, _conflicts, _content, _monitors);

    private static async Task Advance(OnboardingViewModel vm, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await vm.NextCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public void Starts_on_the_welcome_step_with_sensible_defaults()
    {
        var vm = Vm();

        vm.Step.ShouldBe(OnboardingStep.Welcome);
        vm.CanGoBack.ShouldBeFalse();
        vm.NextText.ShouldBe("Get started");
        vm.SelectedStyle.ShouldBe(SetupStyle.SurpriseMe);
        vm.StartWithWindows.ShouldBeTrue();
        vm.SelectedInterval!.Label.ShouldBe("Every 30 minutes");
        vm.StyleOptions.Count.ShouldBe(6);
    }

    [Fact]
    public async Task Steps_advance_in_order_and_can_go_back()
    {
        var vm = Vm();

        await Advance(vm, 1);
        vm.Step.ShouldBe(OnboardingStep.Style);
        vm.StepText.ShouldBe("Step 2 of 5");
        vm.CanGoBack.ShouldBeTrue();

        vm.BackCommand.Execute(null);
        vm.Step.ShouldBe(OnboardingStep.Welcome);

        await Advance(vm, 4);
        vm.Step.ShouldBe(OnboardingStep.Startup);
        vm.NextText.ShouldBe("Finish");
    }

    [Fact]
    public void Choosing_a_style_selects_it_and_highlights_exactly_that_option()
    {
        var vm = Vm();
        vm.StyleOptions.Single(o => o.IsSelected).Style.ShouldBe(SetupStyle.SurpriseMe);

        vm.ChooseStyleCommand.Execute(vm.StyleOptions.First(o => o.Style == SetupStyle.MatteBlack));

        vm.SelectedStyle.ShouldBe(SetupStyle.MatteBlack);
        vm.StyleOptions.Single(o => o.IsSelected).Style.ShouldBe(SetupStyle.MatteBlack);
    }

    [Fact]
    public async Task Games_step_lists_only_installed_catalog_games_alphabetically()
    {
        _installed.Ids = ["cs2", "apex"];
        var vm = Vm();

        await Advance(vm, 3);

        vm.Step.ShouldBe(OnboardingStep.Games);
        vm.Games.Select(g => g.Name).ShouldBe(["Apex Legends", "Counter-Strike 2"]);
        vm.HasGames.ShouldBeTrue();
        vm.ShowGamesEmpty.ShouldBeFalse();
        vm.IsLoadingGames.ShouldBeFalse();
    }

    [Fact]
    public async Task No_installed_games_shows_a_friendly_empty_state_not_a_dead_end()
    {
        var vm = Vm();

        await Advance(vm, 3);

        vm.ShowGamesEmpty.ShouldBeTrue();
        await vm.NextCommand.ExecuteAsync(null);
        vm.Step.ShouldBe(OnboardingStep.Startup);
    }

    [Fact]
    public async Task A_failing_scan_is_treated_as_no_games_found()
    {
        _installed.Throw = true;
        var vm = Vm();

        await Advance(vm, 3);

        vm.ShowGamesEmpty.ShouldBeTrue();
        vm.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task Finishing_applies_style_games_interval_startup_and_marks_onboarding_complete()
    {
        _installed.Ids = ["cs2", "apex"];
        var vm = Vm();
        vm.ChooseStyleCommand.Execute(vm.StyleOptions.First(o => o.Style == SetupStyle.MatteBlack));
        vm.SelectedInterval = vm.IntervalChoices.First(c => c.Value == RotationInterval.Every(TimeSpan.FromHours(1)));
        await Advance(vm, 3);
        vm.Games.First(g => g.Id == "apex").IsEnabled = false;
        await Advance(vm, 1);
        vm.StartWithWindows = false;

        await Advance(vm, 1);

        vm.Step.ShouldBe(OnboardingStep.Done);
        var s = _settings.Current;
        s.Default.Selection.Collections.ShouldBe(["default.matte-black", "default.deep-space", "default.steel-blue-night", "default.architectural"]);
        s.Default.Selection.Excluded.ShouldBe(["ar-01"]);
        s.Default.Mode.ShouldBe(WallpaperMode.Rotate);
        s.Default.Interval.ShouldBe(RotationInterval.Every(TimeSpan.FromHours(1)));
        s.Games["apex"].Enabled.ShouldBeFalse();
        s.Games["cs2"].Enabled.ShouldBeTrue();
        s.General.StartWithWindows.ShouldBeFalse();
        s.General.OnboardingCompleted.ShouldBeTrue();
        _startup.Received(1).Apply(false);
    }

    [Fact]
    public async Task Prefetch_requests_packs_only_for_games_left_on()
    {
        _installed.Ids = ["cs2", "apex"];
        var vm = Vm();
        await Advance(vm, 3);
        vm.Games.First(g => g.Id == "apex").IsEnabled = false;
        await Advance(vm, 2);

        _content.Requested.ShouldBe(["game.cs2"]);
    }

    [Fact]
    public async Task Prefetch_is_skipped_when_disabled_in_settings()
    {
        _settings.Update(s => s.Content.PrefetchInstalledGames = false);
        _installed.Ids = ["cs2"];
        var vm = Vm();

        await Advance(vm, 5);

        _content.Requested.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_wallpaper_mode_uses_the_starter_of_the_first_collection()
    {
        var vm = Vm();
        vm.ChooseStyleCommand.Execute(vm.StyleOptions.First(o => o.Style == SetupStyle.MatteBlack));
        vm.IsRotate = false;

        await Advance(vm, 5);

        _settings.Current.Default.Mode.ShouldBe(WallpaperMode.Fixed);
        _settings.Current.Default.FixedWallpaperId.ShouldBe("mb-02");
    }

    [Fact]
    public async Task Surprise_me_selects_the_starter_wallpapers()
    {
        var vm = Vm();

        await Advance(vm, 5);

        _settings.Current.Default.Selection.Wallpapers.ShouldBe(["mb-02", "ds-01"]);
        _settings.Current.Default.Selection.Collections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Done_then_next_completes_the_flow()
    {
        var vm = Vm();
        var completed = 0;
        vm.Completed += () => completed++;
        await Advance(vm, 5);
        vm.IsDone.ShouldBeTrue();
        vm.NextText.ShouldBe("Done");
        vm.CanGoBack.ShouldBeFalse();

        await Advance(vm, 1);

        completed.ShouldBe(1);
    }

    [Fact]
    public async Task A_save_failure_stays_on_the_last_step_with_a_human_message()
    {
        var settings = Substitute.For<ISettingsProvider>();
        settings.Current.Returns(new AppSettings());
        settings.When(s => s.Update(Arg.Any<Action<AppSettings>>())).Do(_ => throw new IOException("disk full"));
        var vm = new OnboardingViewModel(settings, _catalog, _installed, _startup, _conflicts, _content, _monitors);

        await Advance(vm, 5);

        vm.Step.ShouldBe(OnboardingStep.Startup);
        vm.ErrorMessage.ShouldNotBeNull().ShouldContain("free disk space");
        vm.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public void Spotlight_or_slideshow_backgrounds_are_explained_once_during_onboarding()
    {
        _conflicts.Value = EnvironmentConflict.SpotlightOrSlideshow;

        Vm().ShowSpotlightNote.ShouldBeTrue();
    }

    [Fact]
    public void No_spotlight_note_for_a_normal_background() => Vm().ShowSpotlightNote.ShouldBeFalse();
}

public class AboutViewModelTests
{
    private readonly IAppInfo _info = Substitute.For<IAppInfo>();
    private readonly IUpdateService _updates = Substitute.For<IUpdateService>();
    private readonly FakeCatalogProvider _catalog = new(new CatalogDocument());
    private readonly IExternalLauncher _launcher = Substitute.For<IExternalLauncher>();

    public AboutViewModelTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en-US");
        _info.Version.Returns("1.2.3");
        _info.ThirdPartyNotices.Returns("WPF-UI — MIT");
        _updates.State.Returns(new UpdateState(UpdateStateKind.Idle));
    }

    private AboutViewModel Vm() => new(_info, _updates, _catalog, _launcher, new InlineDispatcher());

    [Fact]
    public void Shows_version_licences_and_the_builtin_disclaimer_when_the_catalog_has_none()
    {
        var vm = Vm();

        vm.VersionText.ShouldBe("Version 1.2.3");
        vm.Licenses.ShouldBe("WPF-UI — MIT");
        vm.Disclaimer.ShouldContain("not affiliated with or endorsed by any game publisher");
    }

    [Fact]
    public void Catalog_disclaimer_wins_when_present()
    {
        _catalog.Current = new CatalogDocument { Disclaimer = "Custom disclaimer." };

        Vm().Disclaimer.ShouldBe("Custom disclaimer.");
    }

    [Theory]
    [InlineData(UpdateStateKind.Checking, "Checking for updates…", false)]
    [InlineData(UpdateStateKind.UpToDate, "You're up to date.", true)]
    [InlineData(UpdateStateKind.Failed, "Couldn't check for updates. Check your connection and try again.", true)]
    [InlineData(UpdateStateKind.NotSupported, "This copy of PrettyDesk can't update itself. Download the newest installer from the project page.", false)]
    public void Update_state_maps_to_human_text_and_command_availability(UpdateStateKind kind, string text, bool canCheck)
    {
        _updates.State.Returns(new UpdateState(kind));

        var vm = Vm();

        vm.UpdateText.ShouldBe(text);
        vm.CanCheck.ShouldBe(canCheck);
        vm.CheckCommand.CanExecute(null).ShouldBe(canCheck);
    }

    [Fact]
    public void Available_downloading_and_ready_states_carry_the_version_and_progress()
    {
        _updates.State.Returns(new UpdateState(UpdateStateKind.Downloading, "1.3.0", 42));
        var vm = Vm();
        vm.UpdateText.ShouldBe("Downloading version 1.3.0… 42%");
        vm.ShowProgress.ShouldBeTrue();
        vm.UpdatePercent.ShouldBe(42);

        _updates.State.Returns(new UpdateState(UpdateStateKind.ReadyToInstall, "1.3.0"));
        _updates.Changed += Raise.Event<Action>();

        vm.UpdateText.ShouldBe("Version 1.3.0 is ready to install.");
        vm.CanRestart.ShouldBeTrue();
        vm.RestartToUpdateCommand.Execute(null);
        _updates.Received(1).ApplyAndRestart();
    }

    [Fact]
    public async Task Check_runs_the_update_service_and_failures_are_friendly()
    {
        var vm = Vm();

        await vm.CheckCommand.ExecuteAsync(null);
        await _updates.Received(1).CheckAsync(Arg.Any<CancellationToken>());

        _updates.CheckAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new HttpRequestException()));
        await vm.CheckCommand.ExecuteAsync(null);

        vm.UpdateText.ShouldContain("Check your connection");
        vm.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public void Links_open_the_project_issue_tracker_and_privacy_pages()
    {
        var vm = Vm();

        vm.OpenProjectPageCommand.Execute(null);
        vm.OpenIssuesCommand.Execute(null);
        vm.OpenPrivacyCommand.Execute(null);

        _launcher.Received(1).OpenUrl(AppLinks.Repository);
        _launcher.Received(1).OpenUrl(AppLinks.Issues);
        _launcher.Received(1).OpenUrl(AppLinks.Privacy);
    }

    [Fact]
    public void Licenses_toggle()
    {
        var vm = Vm();
        vm.ShowLicenses.ShouldBeFalse();

        vm.ToggleLicensesCommand.Execute(null);

        vm.ShowLicenses.ShouldBeTrue();
    }
}

public class ShellAndTrayTests
{
    public ShellAndTrayTests() => Strings.Culture = CultureInfo.GetCultureInfo("en-US");

    private sealed class ShellFixture
    {
        public FakeController Controller { get; } = new();
        public List<string> Created { get; } = [];
        public ShellViewModel Vm { get; }

        public ShellFixture()
        {
            var f = new LibraryFixture();
            Vm = new ShellViewModel(
                () =>
                {
                    Created.Add("home");
                    return new HomeViewModel(Controller, new FakeMonitorProvider(), new FakeContentBrowser(), new FakeConflicts(), new InlineDispatcher(), TimeProvider.System);
                },
                () =>
                {
                    Created.Add("library");
                    return new LibraryViewModel(f.Catalog, f.Settings, f.Content, f.Library, f.Installed, f.Ui, (l, c) => f.NewDetail(l, c), f.NewAdd);
                },
                () => new DefaultsViewModel(f.Settings, f.Catalog, f.Content, f.Library, f.Monitors, f.Files, Substitute.For<IAppController>(), f.Ui),
                () => throw new InvalidOperationException("not used"),
                () => throw new InvalidOperationException("not used"));
        }
    }

    [Fact]
    public void Pages_are_created_lazily_cached_and_released_with_the_window()
    {
        var shell = new ShellFixture();
        shell.Created.ShouldBe(["home"]);
        var home = shell.Vm.CurrentPage.ShouldBeOfType<HomeViewModel>();

        shell.Vm.NavigateToCommand.Execute(PageKind.Library);
        shell.Vm.NavigateToCommand.Execute(PageKind.Home);
        shell.Vm.NavigateToCommand.Execute(PageKind.Library);

        shell.Created.ShouldBe(["home", "library"]);
        shell.Vm.SelectedKind.ShouldBe(PageKind.Library);
        shell.Vm.NavigateToCommand.Execute(PageKind.Home);
        shell.Vm.CurrentPage.ShouldBeSameAs(home);

        shell.Vm.Dispose();

        shell.Vm.CurrentPage.ShouldBeNull();
        shell.Controller.Raise(new OrchestratorStatus { Mode = OrchestratorMode.Paused });
        home.Headline.ShouldNotBe("Paused");
    }

    [Fact]
    public void Navigation_lists_the_five_pages_from_the_spec()
    {
        new ShellFixture().Vm.NavItems.Select(n => n.Label).ShouldBe(["Home", "Library", "Defaults", "Settings", "About"]);
    }

    [Fact]
    public void Tray_shows_status_and_controls_follow_the_mode()
    {
        var controller = new FakeController { Status = new OrchestratorStatus { Mode = OrchestratorMode.Default, DefaultTitle = "Matte Black", IsRotating = true } };
        var app = Substitute.For<IAppController>();
        var vm = new TrayViewModel(controller, app, new InlineDispatcher(), TimeProvider.System);
        vm.StatusLine.ShouldBe("Default · Matte Black (rotating)");
        vm.CanPause.ShouldBeTrue();
        vm.IsPaused.ShouldBeFalse();

        controller.Raise(new OrchestratorStatus { Mode = OrchestratorMode.Paused });

        vm.StatusLine.ShouldBe("Paused");
        vm.IsPaused.ShouldBeTrue();
        vm.CanPause.ShouldBeFalse();
        vm.NextCommand.CanExecute(null).ShouldBeFalse();

        controller.Raise(new OrchestratorStatus { Mode = OrchestratorMode.Game, GameName = "VALORANT" });
        vm.StatusLine.ShouldBe("Playing: VALORANT");
        vm.NextCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Tray_commands_forward_to_the_controller_and_the_app()
    {
        var controller = new FakeController();
        var app = Substitute.For<IAppController>();
        var vm = new TrayViewModel(controller, app, new InlineDispatcher(), TimeProvider.System);

        vm.NextCommand.Execute(null);
        vm.PauseOneHourCommand.Execute(null);
        vm.PauseUntilResumedCommand.Execute(null);
        vm.ResumeCommand.Execute(null);
        vm.OpenCommand.Execute(null);
        vm.QuitCommand.Execute(null);

        controller.Calls.ShouldBe(["next", "pause:60", "pause:forever", "resume"]);
        app.Received(1).ShowMainWindow();
        app.Received(1).Quit();
    }

    [Fact]
    public void Blocked_by_policy_disables_pause_in_the_tray()
    {
        var controller = new FakeController { Status = new OrchestratorStatus { Mode = OrchestratorMode.Blocked } };

        new TrayViewModel(controller, Substitute.For<IAppController>(), new InlineDispatcher(), TimeProvider.System).CanPause.ShouldBeFalse();
    }
}

public sealed class NotificationCoordinatorTests : IDisposable
{
    private readonly FakeSettingsProvider _settings = new();
    private readonly FakeContentBrowser _content = new();
    private readonly IUpdateService _updates = Substitute.For<IUpdateService>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();
    private readonly List<string> _offered = [];
    private readonly NotificationCoordinator _coordinator;

    public NotificationCoordinatorTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en-US");
        var catalog = new FakeCatalogProvider(new CatalogDocument { Games = [new GameEntry { Id = "cs2", DisplayName = "Counter-Strike 2", PackId = "game.cs2" }] });
        _coordinator = new NotificationCoordinator(_settings, catalog, _content, _updates, _notifier, new InlineDispatcher(), _offered.Add);
    }

    public void Dispose() => _coordinator.Dispose();

    [Fact]
    public void Download_failures_notify_once_per_pack_with_the_game_name()
    {
        _content.RaiseFailed("game.cs2");
        _content.RaiseFailed("game.cs2");

        _notifier.Received(1).Show("Couldn't download wallpapers", Arg.Is<string>(b => b.Contains("Counter-Strike 2")), null);
    }

    [Fact]
    public void Download_failure_notifications_can_be_turned_off()
    {
        _settings.Update(s => s.Notifications.DownloadErrors = false);

        _content.RaiseFailed("game.cs2");

        _notifier.DidNotReceiveWithAnyArgs().Show(default!, default!, default);
    }

    [Fact]
    public void Unknown_game_hint_notifies_and_clicking_offers_to_add_it()
    {
        Action? click = null;
        _notifier.When(n => n.Show(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action?>())).Do(c => click = c.ArgAt<Action?>(2));

        _coordinator.OnUnknownGame(new UnknownGameHint("indie.exe"));
        click.ShouldNotBeNull();
        click();

        _notifier.Received(1).Show("Is that a game?", "Add indie.exe to PrettyDesk to give it its own wallpapers.", Arg.Any<Action?>());
        _offered.ShouldBe(["indie.exe"]);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Unknown_game_hints_respect_both_opt_outs(bool hintsOn, bool notifyOn)
    {
        _settings.Update(s =>
        {
            s.Detection.UnknownGameHints = hintsOn;
            s.Notifications.UnknownGames = notifyOn;
        });

        _coordinator.OnUnknownGame(new UnknownGameHint("indie.exe"));

        _notifier.DidNotReceiveWithAnyArgs().Show(default!, default!, default);
    }

    [Fact]
    public void Updates_notify_once_per_version_and_respect_the_setting()
    {
        _updates.State.Returns(new UpdateState(UpdateStateKind.Available, "1.3.0"));
        _updates.Changed += Raise.Event<Action>();
        _updates.Changed += Raise.Event<Action>();
        _updates.State.Returns(new UpdateState(UpdateStateKind.ReadyToInstall, "1.3.0"));
        _updates.Changed += Raise.Event<Action>();

        _notifier.Received(1).Show("PrettyDesk update available", Arg.Any<string>(), null);

        _settings.Update(s => s.Notifications.Updates = false);
        _updates.State.Returns(new UpdateState(UpdateStateKind.Available, "1.4.0"));
        _updates.Changed += Raise.Event<Action>();

        _notifier.Received(1).Show("PrettyDesk update available", Arg.Any<string>(), null);
    }

    [Fact]
    public void Checking_for_updates_never_notifies()
    {
        _updates.State.Returns(new UpdateState(UpdateStateKind.Checking));

        _updates.Changed += Raise.Event<Action>();

        _notifier.DidNotReceiveWithAnyArgs().Show(default!, default!, default);
    }

    [Fact]
    public void A_recovered_settings_file_is_announced()
    {
        _coordinator.OnSettingsRecovered();

        _notifier.Received(1).Show("Your settings were reset", Arg.Any<string>(), null);
    }

    [Fact]
    public void Disposing_unsubscribes()
    {
        _coordinator.Dispose();

        _content.RaiseFailed("game.cs2");

        _notifier.DidNotReceiveWithAnyArgs().Show(default!, default!, default);
    }
}
