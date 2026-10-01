using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Settings;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;
using static PrettyDesk.Core.Tests.Support.TestData;

namespace PrettyDesk.Core.Tests.Detection;

public sealed class UnknownGameMonitorTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero));
    private readonly FakeSettings _settings = new();
    private readonly StubForeground _foreground = new();
    private readonly UserNotificationState[] _state = [UserNotificationState.RunningD3DFullScreen];
    private readonly List<UnknownGameHint> _hints = [];
    private readonly UnknownGameMonitor _monitor;

    public UnknownGameMonitorTests()
    {
        _foreground.Current = new ForegroundInfo(5, "indie.exe");
        _monitor = new UnknownGameMonitor(_foreground, () => _state[0], exe => KnownExecutables.IsKnown(new CatalogDocument { Games = [Game("cs2", configure: b => b.Exe.Add("cs2.exe"))], ExcludeExeNames = ["steam.exe"] }, _settings.Current, exe), new UnknownGameHinter(), _settings, _time);
        _monitor.HintAvailable += _hints.Add;
    }

    public void Dispose() => _monitor.Dispose();

    private void Sample(int seconds)
    {
        for (var i = 0; i < seconds; i += 5)
        {
            _monitor.Tick();
            _time.Advance(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void Raises_a_hint_after_a_minute_of_full_screen_for_an_unknown_exe()
    {
        Sample(55);
        _hints.ShouldBeEmpty();

        Sample(15);

        _hints.ShouldHaveSingleItem().ExeName.ShouldBe("indie.exe");
    }

    [Fact]
    public void Known_catalog_games_and_launchers_never_raise_a_hint()
    {
        _foreground.Current = new ForegroundInfo(5, "cs2.exe");
        Sample(300);
        _foreground.Current = new ForegroundInfo(6, "steam.exe");
        Sample(300);

        _hints.ShouldBeEmpty();
    }

    [Fact]
    public void A_game_the_user_disabled_still_counts_as_known()
    {
        _settings.Update(s => s.GetGame("cs2").Enabled = false);
        _foreground.Current = new ForegroundInfo(5, "cs2.exe");

        Sample(300);

        _hints.ShouldBeEmpty();
    }

    [Fact]
    public void Custom_games_count_as_known()
    {
        _settings.Update(s => s.CustomGames.Add(new CustomGame { Id = "c", DisplayName = "C", ExeNames = ["indie.exe"] }));

        Sample(300);

        _hints.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Each_opt_out_silences_hints(bool hints, bool notify, bool detection)
    {
        _settings.Update(s =>
        {
            s.Detection.UnknownGameHints = hints;
            s.Notifications.UnknownGames = notify;
            s.Detection.Enabled = detection;
        });

        Sample(300);

        _hints.ShouldBeEmpty();
    }

    [Fact]
    public void Timer_samples_every_five_seconds_once_started()
    {
        _monitor.Start();

        // Step in sample-sized increments so each timer callback observes its own point in time.
        for (var i = 0; i < 15; i++)
        {
            _time.Advance(UnknownGameMonitor.SampleInterval);
        }

        SpinWait.SpinUntil(() => _hints.Count > 0, TimeSpan.FromSeconds(5)).ShouldBeTrue();
    }

    [Fact]
    public void No_foreground_app_is_ignored()
    {
        _foreground.Current = null;

        Sample(300);

        _hints.ShouldBeEmpty();
    }

    private sealed class StubForeground : IForegroundSource
    {
        public ForegroundInfo? Current { get; set; }

#pragma warning disable CS0067 // Required by the interface; this stub never raises it.
        public event Action? Changed;
#pragma warning restore CS0067
    }
}
