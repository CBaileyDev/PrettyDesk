using Microsoft.Win32;
using PrettyDesk.Windows;
using Shouldly;
using Xunit;

namespace PrettyDesk.Windows.Tests;

public class EnvironmentTests
{
    [Fact]
    public void Run_key_toggle_round_trips_with_the_background_argument()
    {
        WindowsOnly.Require();
        var name = "PrettyDesk.Test." + Guid.NewGuid().ToString("N");
        var startup = new StartupRegistration(name);
        try
        {
            startup.IsEnabled.ShouldBeFalse();

            startup.Enable(@"C:\Apps\PrettyDesk\PrettyDesk.exe");

            startup.IsEnabled.ShouldBeTrue();
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath)!;
            key.GetValue(name).ShouldBe("\"C:\\Apps\\PrettyDesk\\PrettyDesk.exe\" --background");
            startup.IsStale(@"C:\Apps\PrettyDesk\PrettyDesk.exe").ShouldBeFalse();
            startup.IsStale(@"D:\Moved\PrettyDesk.exe").ShouldBeTrue();

            startup.Disable();

            startup.IsEnabled.ShouldBeFalse();
        }
        finally
        {
            startup.Disable();
        }
    }

    [Fact]
    public void System_state_reads_without_throwing_and_reports_plausible_values()
    {
        WindowsOnly.Require();
        using var state = new WindowsSystemState(TimeProvider.System);

        Should.NotThrow(() => (state.IsLightTheme, state.IsBatterySaverOn, state.IsWallpaperPolicyLocked));
        WindowsSystemState.ReadBackgroundType().ShouldBeOneOf("picture", "solid", "slideshow", "spotlight");
    }

    [Fact]
    public void Conflict_detector_runs_against_the_live_process_list()
    {
        WindowsOnly.Require();
        using var state = new WindowsSystemState(TimeProvider.System);

        Should.NotThrow(() => new EnvironmentConflictDetector(new ProcessSource(), state).Detect());
    }

    [Fact]
    public void Steam_watcher_starts_and_stops_cleanly_even_without_steam_installed()
    {
        WindowsOnly.Require();
        var watcher = new SteamRunningAppWatcher();

        watcher.Start();
        Should.NotThrow(() => watcher.CurrentAppId);
        Should.NotThrow(watcher.Dispose);
    }

    [Fact]
    public void Foreground_watcher_starts_resolves_and_stops_cleanly()
    {
        WindowsOnly.Require();
        var watcher = new ForegroundWatcher(new ProcessSource(), new ProcessDetailsSource());

        watcher.Start();
        Should.NotThrow(() => watcher.Current);
        Should.NotThrow(watcher.Dispose);
    }

    [Fact]
    public void Shell_message_window_starts_and_stops_cleanly()
    {
        WindowsOnly.Require();
        var window = new ShellMessageWindow();

        window.Start();
        Should.NotThrow(window.Dispose);
    }

    [Fact]
    public void User_notification_state_query_returns_a_defined_value()
    {
        WindowsOnly.Require();

        Enum.IsDefined(UserNotificationStateSource.Query()).ShouldBeTrue();
    }
}
