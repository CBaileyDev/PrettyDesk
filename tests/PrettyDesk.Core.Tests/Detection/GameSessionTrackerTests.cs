using PrettyDesk.Core.Detection;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Detection;

public class GameSessionTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly GameSessionTracker _tracker = new(DetectionOptions.Default);
    private readonly List<ActiveGameChange> _changes = [];

    public GameSessionTrackerTests() => _tracker.ActiveChanged += _changes.Add;

    private static GameMatch M(string id, params int[] pids) => new(id, pids, ViaSteam: false);

    private void At(double seconds, int foreground = 0, params GameMatch[] matches) =>
        _tracker.Observe(T0 + TimeSpan.FromSeconds(seconds), matches, foreground);

    [Fact]
    public void Game_becomes_active_only_after_the_detect_delay()
    {
        At(0, 0, M("g", 1));
        At(2.9, 0, M("g", 1));
        _tracker.Active.ShouldBeNull();

        At(3, 0, M("g", 1));

        _tracker.Active.ShouldBe(new ActiveGame("g", InGrace: false));
        _changes.ShouldHaveSingleItem().Current!.GameId.ShouldBe("g");
    }

    [Fact]
    public void Crash_on_launch_never_activates_and_forgets_immediately()
    {
        At(0, 0, M("g", 1));
        At(1, 0, M("g", 1));
        At(2);

        _tracker.NextDeadline.ShouldBeNull();
        At(10, 0, M("g", 2));
        At(12.9, 0, M("g", 2));
        _tracker.Active.ShouldBeNull();
        At(13, 0, M("g", 2));
        _tracker.Active!.GameId.ShouldBe("g");
    }

    [Fact]
    public void Exit_keeps_the_game_in_grace_then_clears_after_exit_grace()
    {
        At(0, 0, M("g", 1));
        At(3, 0, M("g", 1));
        At(20, 0, M("g", 1));

        At(21);
        _tracker.Active.ShouldBe(new ActiveGame("g", InGrace: true));

        At(29.9);
        _tracker.Active.ShouldNotBeNull();

        At(30);
        _tracker.Active.ShouldBeNull();
        _changes.Count.ShouldBe(3);
    }

    [Fact]
    public void Self_restart_within_grace_keeps_the_context_without_redebouncing()
    {
        At(0, 0, M("g", 1));
        At(3, 0, M("g", 1));
        At(10, 0, M("g", 1));
        At(11);
        At(18, 0, M("g", 9));

        _tracker.Active.ShouldBe(new ActiveGame("g", InGrace: false));
        _changes.Select(c => c.Current).ShouldBe([new ActiveGame("g", false), new ActiveGame("g", true), new ActiveGame("g", false)]);
    }

    [Fact]
    public void Restart_after_grace_must_debounce_again()
    {
        At(0, 0, M("g", 1));
        At(3, 0, M("g", 1));
        At(4);
        At(15);
        _tracker.Active.ShouldBeNull();

        At(16, 0, M("g", 2));
        _tracker.Active.ShouldBeNull();
        At(19, 0, M("g", 2));
        _tracker.Active!.GameId.ShouldBe("g");
    }

    [Fact]
    public void Foreground_game_wins_when_two_games_run()
    {
        At(0, 100, M("a", 100), M("b", 200));
        At(3, 100, M("a", 100), M("b", 200));
        _tracker.Active!.GameId.ShouldBe("a");

        At(5, 200, M("a", 100), M("b", 200));
        _tracker.Active!.GameId.ShouldBe("b");

        At(8, 100, M("a", 100), M("b", 200));
        _tracker.Active!.GameId.ShouldBe("a");
    }

    [Fact]
    public void Most_recent_start_breaks_a_foreground_tie()
    {
        At(0, 0, M("old", 1));
        At(2, 0, M("old", 1), M("new", 2));
        At(5, 0, M("old", 1), M("new", 2));

        _tracker.Active!.GameId.ShouldBe("new");
    }

    [Fact]
    public void Running_game_outranks_a_game_in_grace()
    {
        At(0, 1, M("a", 1));
        At(3, 1, M("a", 1));
        At(4, 0, M("b", 2));
        At(7, 0, M("b", 2));

        _tracker.Active.ShouldBe(new ActiveGame("b", InGrace: false));
    }

    [Fact]
    public void Steam_only_match_behaves_like_a_process_match()
    {
        var steam = new GameMatch("cs2", [], ViaSteam: true);
        At(0, 0, steam);
        At(3, 0, steam);
        _tracker.Active!.GameId.ShouldBe("cs2");

        At(4);
        _tracker.Active!.InGrace.ShouldBeTrue();
    }

    [Fact]
    public void Steam_app_flip_switches_games_after_the_new_one_debounces()
    {
        At(0, 0, new GameMatch("a", [], true));
        At(3, 0, new GameMatch("a", [], true));
        At(4, 0, new GameMatch("b", [], true));
        At(7, 0, new GameMatch("b", [], true));

        _tracker.Active.ShouldBe(new ActiveGame("b", false));
    }

    [Fact]
    public void Next_deadline_tracks_debounce_then_grace()
    {
        At(0, 0, M("g", 1));
        _tracker.NextDeadline.ShouldBe(T0 + TimeSpan.FromSeconds(3));

        At(3, 0, M("g", 1));
        _tracker.NextDeadline.ShouldBeNull();

        At(5);
        _tracker.NextDeadline.ShouldBe(T0 + TimeSpan.FromSeconds(3 + 10));
    }

    [Fact]
    public void Reset_clears_the_active_game()
    {
        At(0, 0, M("g", 1));
        At(3, 0, M("g", 1));

        _tracker.Reset();

        _tracker.Active.ShouldBeNull();
        _changes.Last().Current.ShouldBeNull();
    }

    [Fact]
    public void Custom_timings_are_honoured()
    {
        var tracker = new GameSessionTracker(new DetectionOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        tracker.Observe(T0, [M("g", 1)], 0);
        tracker.Observe(T0 + TimeSpan.FromSeconds(1), [M("g", 1)], 0);
        tracker.Active.ShouldNotBeNull();

        tracker.Observe(T0 + TimeSpan.FromSeconds(2), [], 0);
        tracker.Observe(T0 + TimeSpan.FromSeconds(4), [], 0);
        tracker.Active.ShouldBeNull();
    }

    [Fact]
    public void No_event_fires_when_nothing_changes()
    {
        At(0, 0, M("g", 1));
        At(3, 0, M("g", 1));
        At(4, 0, M("g", 1));
        At(5, 0, M("g", 1));

        _changes.Count.ShouldBe(1);
    }
}
