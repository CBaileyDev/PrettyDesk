using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Presentation.Tests.Support;
using PrettyDesk.Presentation.ViewModels;
using Shouldly;
using Xunit;

namespace PrettyDesk.Presentation.Tests.ViewModels;

public class SettingsSectionTests
{
    private readonly FakeSettingsProvider _settings = new();
    private readonly InlineDispatcher _ui = new();

    public SettingsSectionTests() => Strings.Culture = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Personalization_is_opt_in_and_persists_without_loading_writes()
    {
        using var vm = new GeneralSettingsViewModel(_settings, Substitute.For<IStartupService>(), _ui);
        vm.Theme.ShouldBe(AppThemePreference.System);
        vm.DesktopClock.ShouldBeFalse();
        vm.DesktopNowPlaying.ShouldBeFalse();
        vm.DesktopVisualizer.ShouldBeFalse();
        _settings.UpdateCount.ShouldBe(0);
        vm.Theme = AppThemePreference.Dark;
        vm.DesktopClock = true;
        vm.DesktopNowPlaying = true;
        vm.DesktopVisualizer = true;
        _settings.Current.General.Theme.ShouldBe(AppThemePreference.Dark);
        _settings.Current.General.DesktopClock.ShouldBeTrue();
        _settings.Current.General.DesktopNowPlaying.ShouldBeTrue();
        _settings.Current.General.DesktopVisualizer.ShouldBeTrue();
        _settings.UpdateCount.ShouldBe(4);
    }

    [Fact]
    public void General_loads_defaults_and_saves_changes()
    {
        var startup = Substitute.For<IStartupService>();
        startup.IsEnabled.Returns(true);
        var vm = new GeneralSettingsViewModel(_settings, startup, _ui);
        vm.StartWithWindows.ShouldBeTrue();
        _settings.UpdateCount.ShouldBe(0);

        vm.RestoreOnExit = true;

        _settings.Current.General.RestoreOnExit.ShouldBeTrue();
        _settings.UpdateCount.ShouldBe(1);
    }

    [Fact]
    public void Beta_channel_is_off_by_default_and_the_choice_is_saved()
    {
        var vm = new GeneralSettingsViewModel(_settings, Substitute.For<IStartupService>(), _ui);
        vm.BetaUpdates.ShouldBeFalse();

        vm.BetaUpdates = true;

        _settings.Current.General.BetaUpdates.ShouldBeTrue();
    }

    [Fact]
    public void Toggling_start_with_windows_updates_the_run_key_service_and_settings()
    {
        var startup = Substitute.For<IStartupService>();
        startup.IsEnabled.Returns(true);
        var vm = new GeneralSettingsViewModel(_settings, startup, _ui);

        vm.StartWithWindows = false;

        startup.Received(1).Apply(false);
        _settings.Current.General.StartWithWindows.ShouldBeFalse();
    }

    [Fact]
    public void A_registry_failure_is_shown_as_a_message_not_thrown()
    {
        var startup = Substitute.For<IStartupService>();
        startup.IsEnabled.Returns(false);
        startup.When(s => s.Apply(Arg.Any<bool>())).Do(_ => throw new UnauthorizedAccessException());
        var vm = new GeneralSettingsViewModel(_settings, startup, _ui);

        Should.NotThrow(() => vm.StartWithWindows = false);
        Should.NotThrow(() => vm.StartWithWindows = true);

        vm.HasError.ShouldBeTrue();
    }

    [Fact]
    public void External_changes_reload_without_writing_back()
    {
        var vm = new NotificationSettingsViewModel(_settings, _ui);
        var before = _settings.UpdateCount;

        _settings.ChangeExternally(s => s.Notifications.Updates = false);

        vm.Updates.ShouldBeFalse();
        _settings.UpdateCount.ShouldBe(before + 1);
    }

    [Fact]
    public void Detection_values_are_clamped_to_their_documented_ranges()
    {
        var vm = new DetectionSettingsViewModel(_settings, _ui);

        vm.PollSeconds = 99;
        vm.PollSeconds.ShouldBe(10);
        _settings.Current.Detection.PollSeconds.ShouldBe(10);

        vm.PollSeconds = 0;
        vm.PollSeconds.ShouldBe(1);

        vm.DetectDelaySeconds = 500;
        vm.DetectDelaySeconds.ShouldBe(DetectionSettingsViewModel.MaxDetectDelaySeconds);

        vm.ExitGraceSeconds = -4;
        vm.ExitGraceSeconds.ShouldBe(0);
        _settings.Current.Detection.ExitGraceSeconds.ShouldBe(0);
    }

    [Fact]
    public void Detection_text_labels_follow_the_values()
    {
        var vm = new DetectionSettingsViewModel(_settings, _ui);

        vm.PollSeconds = 5;

        vm.PollSecondsText.ShouldBe("5 s");
    }

    [Fact]
    public void Detection_can_be_turned_off_and_hints_toggled()
    {
        var vm = new DetectionSettingsViewModel(_settings, _ui);

        vm.Enabled = false;
        vm.UnknownGameHints = false;

        _settings.Current.Detection.Enabled.ShouldBeFalse();
        _settings.Current.Detection.UnknownGameHints.ShouldBeFalse();
    }

    [Fact]
    public void Monitor_options_map_to_the_settings_enum()
    {
        var vm = new MonitorSettingsViewModel(_settings, _ui);
        vm.DifferentPerMonitor.ShouldBeFalse();

        vm.DifferentPerMonitor = true;
        vm.GameOnSecondaryOnly = true;

        _settings.Current.Monitors.Mode.ShouldBe(MonitorMode.Different);
        _settings.Current.Monitors.GameOnSecondaryOnly.ShouldBeTrue();

        vm.DifferentPerMonitor = false;
        _settings.Current.Monitors.Mode.ShouldBe(MonitorMode.Same);
    }

    [Fact]
    public async Task Clearing_downloads_confirms_runs_busy_and_reports_the_freed_space()
    {
        var content = new FakeContentBrowser { Usage = 2L * 1024 * 1024 * 1024, Freed = 1_572_864 };
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(true);
        var protectedPacks = Substitute.For<IDetectionProtectedPacks>();
        protectedPacks.PacksInUse().Returns(new HashSet<string> { "default.matte-black" });
        var vm = new StorageSettingsViewModel(_settings, content, dialogs, protectedPacks, _ui);
        vm.UsageText.ShouldBe("Downloaded wallpapers use 2 GB");

        await vm.ClearCommand.ExecuteAsync(null);

        vm.ResultMessage.ShouldBe("Freed 1.5 MB.");
        vm.UsageText.ShouldBe("Downloaded wallpapers use 0 B");
        vm.IsBusy.ShouldBeFalse();
        vm.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task Declining_the_clear_confirmation_does_nothing()
    {
        var content = new FakeContentBrowser { Usage = 500, Freed = 500 };
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(false);
        var vm = new StorageSettingsViewModel(_settings, content, dialogs, Substitute.For<IDetectionProtectedPacks>(), _ui);

        await vm.ClearCommand.ExecuteAsync(null);

        vm.ResultMessage.ShouldBeNull();
        content.Usage.ShouldBe(500);
    }

    [Fact]
    public void Storage_cap_and_prefetch_persist()
    {
        var vm = new StorageSettingsViewModel(_settings, new FakeContentBrowser(), Substitute.For<IDialogService>(), Substitute.For<IDetectionProtectedPacks>(), _ui);

        vm.MaxCacheGb = 10;
        vm.PrefetchInstalledGames = false;

        _settings.Current.Content.MaxCacheGB.ShouldBe(10);
        _settings.Current.Content.PrefetchInstalledGames.ShouldBeFalse();
        vm.CapText.ShouldBe("10 GB");
        StorageSettingsViewModel.CapChoicesGb.ShouldContain(3);
    }
}

public class AdvancedAndRestoreTests
{
    private readonly FakeDetectionFeed _feed = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 20, 15, 30, TimeSpan.Zero));
    private readonly IExternalLauncher _launcher = Substitute.For<IExternalLauncher>();
    private readonly IAppInfo _info = Substitute.For<IAppInfo>();
    private readonly IDiagnosticsExporter _exporter = Substitute.For<IDiagnosticsExporter>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IAppMaintenance _maintenance = Substitute.For<IAppMaintenance>();

    public AdvancedAndRestoreTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en-US");
        _info.LogsDirectory.Returns(@"C:\Logs");
    }

    private AdvancedSettingsViewModel Create() => new(_feed, new InlineDispatcher(), _time, _launcher, _info, _exporter, _dialogs, _maintenance);

    [Fact]
    public void Detection_log_shows_the_foreground_exe_and_catalog_matches()
    {
        var vm = Create();

        _feed.Raise("cs2.exe", "cs2");
        _feed.Raise("notepad.exe");

        vm.Log.Count.ShouldBe(2);
        vm.Log[0].Text.ShouldContain("notepad.exe");
        vm.Log[0].Text.ShouldContain("no catalog match");
        vm.Log[1].Text.ShouldContain("cs2.exe");
        vm.Log[1].Text.ShouldContain("matches: cs2");
        vm.HasLog.ShouldBeTrue();
    }

    [Fact]
    public void Unchanged_observations_do_not_flood_the_log()
    {
        var vm = Create();

        for (var i = 0; i < 20; i++)
        {
            _feed.Raise("same.exe");
        }

        vm.Log.Count.ShouldBe(1);
    }

    [Fact]
    public void Log_is_capped_and_can_be_paused_and_cleared()
    {
        var vm = Create();
        for (var i = 0; i < AdvancedSettingsViewModel.MaxLogEntries + 25; i++)
        {
            _feed.Raise($"app{i}.exe");
        }

        vm.Log.Count.ShouldBe(AdvancedSettingsViewModel.MaxLogEntries);

        vm.LogPaused = true;
        _feed.Raise("ignored.exe");
        vm.Log.ShouldNotContain(e => e.Text.Contains("ignored.exe", StringComparison.Ordinal));

        vm.ClearLogCommand.Execute(null);
        vm.Log.ShouldBeEmpty();
        vm.HasLog.ShouldBeFalse();
    }

    [Fact]
    public void Foreground_less_observation_is_labelled()
    {
        var vm = Create();

        _feed.Raise(null);

        vm.Log[0].Text.ShouldContain("(no foreground app)");
    }

    [Fact]
    public void Log_is_never_persisted_it_only_lives_in_the_view_model()
    {
        // Structural guard: the VM has no dependency that could write it (no settings/state/file service in its constructor).
        typeof(AdvancedSettingsViewModel).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name)
            .ShouldNotContain(n => n.Contains("Settings", StringComparison.Ordinal) || n.Contains("State", StringComparison.Ordinal));
    }

    [Fact]
    public void Open_logs_uses_the_logs_folder()
    {
        Create().OpenLogsCommand.Execute(null);

        _launcher.Received(1).OpenFolder(@"C:\Logs");
    }

    [Fact]
    public async Task Export_reports_the_path_and_offers_show_in_folder()
    {
        var zip = Path.Combine("users", "me", "Desktop", "PrettyDesk-diagnostics.zip");
        _exporter.ExportAsync(default).ReturnsForAnyArgs(zip);
        var vm = Create();

        await vm.ExportCommand.ExecuteAsync(null);
        vm.ShowExportCommand.Execute(null);

        vm.ResultMessage.ShouldBe("Saved to " + zip);
        vm.IsBusy.ShouldBeFalse();
        _launcher.Received(1).OpenFolder(Path.GetDirectoryName(zip)!);
    }

    [Fact]
    public async Task Export_failure_is_a_human_message_with_a_next_step()
    {
        _exporter.ExportAsync(default).ThrowsAsyncForAnyArgs(new IOException("disk full"));
        var vm = Create();

        await vm.ExportCommand.ExecuteAsync(null);

        vm.ErrorMessage!.ShouldContain("try again");
        vm.ErrorMessage!.ShouldNotContain("disk full");
        vm.IsBusy.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task Reset_asks_whether_to_remove_the_users_images_too(int choice, bool removeImages)
    {
        _dialogs.ChooseAsync(default!, default!, default!).ReturnsForAnyArgs(choice);
        var vm = Create();

        await vm.ResetCommand.ExecuteAsync(null);

        await _maintenance.Received(1).ResetAsync(removeImages, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task Cancelling_the_reset_dialog_resets_nothing(int choice)
    {
        _dialogs.ChooseAsync(default!, default!, default!).ReturnsForAnyArgs(choice);

        await Create().ResetCommand.ExecuteAsync(null);

        await _maintenance.DidNotReceiveWithAnyArgs().ResetAsync(default, default);
    }

    [Fact]
    public async Task Reset_failure_is_reported_in_human_words()
    {
        _dialogs.ChooseAsync(default!, default!, default!).ReturnsForAnyArgs(0);
        _maintenance.ResetAsync(default, default).ThrowsAsyncForAnyArgs(new IOException());
        var vm = Create();

        await vm.ResetCommand.ExecuteAsync(null);

        vm.ErrorMessage!.ShouldContain("Close any program");
    }

    [Fact]
    public async Task Restore_confirms_then_reports_success_nothing_to_restore_or_a_note()
    {
        var restore = Substitute.For<IRestoreService>();
        _dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(true);
        var vm = new RestoreViewModel(restore, _dialogs);

        restore.RestoreOriginalAsync(default).ReturnsForAnyArgs(new RestoreOutcome(false, 1, null));
        await vm.RestoreCommand.ExecuteAsync(null);
        vm.ResultMessage.ShouldBe("Your original wallpaper is back.");

        restore.RestoreOriginalAsync(default).ReturnsForAnyArgs(new RestoreOutcome(true, 0, null));
        await vm.RestoreCommand.ExecuteAsync(null);
        vm.ResultMessage!.ShouldContain("no saved original");

        restore.RestoreOriginalAsync(default).ReturnsForAnyArgs(new RestoreOutcome(false, 1, "It was Spotlight."));
        await vm.RestoreCommand.ExecuteAsync(null);
        vm.ResultMessage.ShouldBe("It was Spotlight.");
    }

    [Fact]
    public async Task Restore_declined_or_failed()
    {
        var restore = Substitute.For<IRestoreService>();
        var vm = new RestoreViewModel(restore, _dialogs);

        _dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(false);
        await vm.RestoreCommand.ExecuteAsync(null);
        await restore.DidNotReceiveWithAnyArgs().RestoreOriginalAsync(default);

        _dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(true);
        restore.RestoreOriginalAsync(default).ThrowsAsyncForAnyArgs(new InvalidOperationException());
        await vm.RestoreCommand.ExecuteAsync(null);
        vm.ErrorMessage!.ShouldContain("Try again");
    }

    [Fact]
    public void Disposing_the_advanced_page_stops_the_feed_subscription()
    {
        var vm = Create();
        vm.Dispose();

        _feed.Raise("after.exe");

        vm.Log.ShouldBeEmpty();
    }
}
