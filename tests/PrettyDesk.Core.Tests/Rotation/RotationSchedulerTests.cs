using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Core.Rotation;
using PrettyDesk.Core.Settings;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Rotation;

public class RotationSchedulerTests
{
    private static readonly string[] Pool = ["a", "b", "c", "d"];
    private static readonly RotationPolicy Every30 = new(RotationOrder.Shuffle, RotationInterval.Every(TimeSpan.FromMinutes(30)));

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, ContextRotationState> _states = [];

    private RotationScheduler Create(int seed = 1) => new(_states, _time, new Random(seed));

    [Fact]
    public void First_resolve_draws_and_second_resolve_is_stable_until_the_deadline()
    {
        var scheduler = Create();

        var first = scheduler.Resolve("default", Pool, Every30);
        _time.Advance(TimeSpan.FromMinutes(29));
        var second = scheduler.Resolve("default", Pool, Every30);

        first.Changed.ShouldBeTrue();
        second.WallpaperId.ShouldBe(first.WallpaperId);
        second.Changed.ShouldBeFalse();
    }

    [Fact]
    public void Advances_exactly_when_the_deadline_passes()
    {
        var scheduler = Create();
        var first = scheduler.Resolve("default", Pool, Every30).WallpaperId;

        _time.Advance(TimeSpan.FromMinutes(30));
        var next = scheduler.Resolve("default", Pool, Every30);

        next.Changed.ShouldBeTrue();
        next.WallpaperId.ShouldNotBe(first);
        scheduler.NextDeadline("default", Every30).ShouldBe(_time.GetUtcNow() + TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Suspend_of_three_days_rotates_exactly_once()
    {
        var scheduler = Create();
        scheduler.Resolve("default", Pool, Every30);

        _time.Advance(TimeSpan.FromDays(3));
        var afterResume = scheduler.Resolve("default", Pool, Every30);
        var again = scheduler.Resolve("default", Pool, Every30);

        afterResume.Changed.ShouldBeTrue();
        again.Changed.ShouldBeFalse();
        again.WallpaperId.ShouldBe(afterResume.WallpaperId);
    }

    [Fact]
    public void Game_context_does_not_advance_default_and_default_resumes_unchanged()
    {
        var scheduler = Create();
        var shown = scheduler.Resolve("default", Pool, Every30).WallpaperId;
        var gamePolicy = new RotationPolicy(RotationOrder.Shuffle, RotationInterval.Session);

        _time.Advance(TimeSpan.FromMinutes(10));
        scheduler.EnterContext("game:cs2", ["g1", "g2"], gamePolicy);
        _time.Advance(TimeSpan.FromMinutes(10));

        scheduler.Resolve("default", Pool, Every30).WallpaperId.ShouldBe(shown);
    }

    [Fact]
    public void Default_advances_once_after_a_game_when_its_interval_elapsed_meanwhile()
    {
        var scheduler = Create();
        var shown = scheduler.Resolve("default", Pool, Every30).WallpaperId;

        _time.Advance(TimeSpan.FromHours(3));
        var resumed = scheduler.Resolve("default", Pool, Every30);

        resumed.Changed.ShouldBeTrue();
        resumed.WallpaperId.ShouldNotBe(shown);
    }

    [Fact]
    public void Session_interval_never_rotates_mid_session_but_advances_on_the_next_session()
    {
        var scheduler = Create();
        var policy = new RotationPolicy(RotationOrder.Shuffle, RotationInterval.Session);
        var first = scheduler.EnterContext("game:g", Pool, policy).WallpaperId;

        _time.Advance(TimeSpan.FromHours(6));
        scheduler.Resolve("game:g", Pool, policy).WallpaperId.ShouldBe(first);

        var second = scheduler.EnterContext("game:g", Pool, policy);
        second.Changed.ShouldBeTrue();
        second.WallpaperId.ShouldNotBe(first);
        scheduler.NextDeadline("game:g", policy).ShouldBeNull();
    }

    [Fact]
    public void Unlock_interval_rotates_on_unlock_only()
    {
        var scheduler = Create();
        var policy = new RotationPolicy(RotationOrder.Shuffle, RotationInterval.Unlock);
        var first = scheduler.Resolve("default", Pool, policy).WallpaperId;

        _time.Advance(TimeSpan.FromHours(5));
        scheduler.Resolve("default", Pool, policy).WallpaperId.ShouldBe(first);

        scheduler.NotifyUnlock();
        var after = scheduler.Resolve("default", Pool, policy);
        after.Changed.ShouldBeTrue();
        scheduler.Resolve("default", Pool, policy).Changed.ShouldBeFalse();
    }

    [Fact]
    public void Battery_saver_pauses_rotation_but_not_the_initial_draw()
    {
        var scheduler = Create();
        var paused = Every30 with { AutoRotationPaused = true };

        var first = scheduler.Resolve("default", Pool, paused);
        _time.Advance(TimeSpan.FromHours(2));
        var later = scheduler.Resolve("default", Pool, paused);

        first.WallpaperId.ShouldNotBeNull();
        later.Changed.ShouldBeFalse();
        scheduler.NextDeadline("default", paused).ShouldBeNull();

        scheduler.Resolve("default", Pool, Every30).Changed.ShouldBeTrue();
    }

    [Fact]
    public void Sequential_order_walks_the_pool_and_wraps()
    {
        var scheduler = Create();
        var policy = new RotationPolicy(RotationOrder.Sequential, RotationInterval.Every(TimeSpan.FromMinutes(5)));
        var seen = new List<string?> { scheduler.Resolve("default", Pool, policy).WallpaperId };

        for (var i = 0; i < 5; i++)
        {
            _time.Advance(TimeSpan.FromMinutes(5));
            seen.Add(scheduler.Resolve("default", Pool, policy).WallpaperId);
        }

        seen.ShouldBe(["a", "b", "c", "d", "a", "b"]);
    }

    [Fact]
    public void Manual_advance_changes_wallpaper_and_restarts_the_timer()
    {
        var scheduler = Create();
        var first = scheduler.Resolve("default", Pool, Every30).WallpaperId;
        _time.Advance(TimeSpan.FromMinutes(20));

        var next = scheduler.Advance("default", Pool, Every30);

        next.WallpaperId.ShouldNotBe(first);
        scheduler.NextDeadline("default", Every30).ShouldBe(_time.GetUtcNow() + TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Current_wallpaper_leaving_the_pool_triggers_a_new_draw()
    {
        var scheduler = Create();
        var first = scheduler.Resolve("default", Pool, Every30).WallpaperId!;

        var result = scheduler.Resolve("default", Pool.Where(p => p != first).ToList(), Every30);

        result.Changed.ShouldBeTrue();
        result.WallpaperId.ShouldNotBe(first);
    }

    [Fact]
    public void Empty_pool_returns_nothing_and_keeps_state()
    {
        var scheduler = Create();
        var first = scheduler.Resolve("default", Pool, Every30).WallpaperId;

        scheduler.Resolve("default", [], Every30).WallpaperId.ShouldBeNull();
        scheduler.Current("default").ShouldBe(first);
    }

    [Fact]
    public void Position_survives_a_serialisation_round_trip()
    {
        var scheduler = Create(seed: 5);
        scheduler.Resolve("default", Pool, Every30);
        _time.Advance(TimeSpan.FromMinutes(31));
        scheduler.Resolve("default", Pool, Every30);

        var state = new AppState { Rotation = _states };
        var json = JsonSerializer.Serialize(state, AppStateJsonContext.Default.AppState);
        var restored = JsonSerializer.Deserialize(json, AppStateJsonContext.Default.AppState)!;

        var restoredScheduler = new RotationScheduler(restored.Rotation, _time, new Random(99));
        restoredScheduler.Current("default").ShouldBe(scheduler.Current("default"));
        restoredScheduler.Resolve("default", Pool, Every30).Changed.ShouldBeFalse();
        restoredScheduler.NextDeadline("default", Every30).ShouldBe(scheduler.NextDeadline("default", Every30));

        // The restored bag has 2 left in the cycle; the next two draws must be exactly those.
        var expected = restored.Rotation["default"].Bag.Remaining.ToHashSet();
        _time.Advance(TimeSpan.FromMinutes(31));
        var a = restoredScheduler.Resolve("default", Pool, Every30).WallpaperId!;
        _time.Advance(TimeSpan.FromMinutes(31));
        var b = restoredScheduler.Resolve("default", Pool, Every30).WallpaperId!;
        new HashSet<string> { a, b }.ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void Clock_moving_backwards_does_not_freeze_rotation()
    {
        var clock = new SettableTimeProvider(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var scheduler = new RotationScheduler(_states, clock, new Random(1));
        scheduler.Resolve("default", Pool, Every30);

        clock.Now -= TimeSpan.FromHours(5);
        scheduler.Resolve("default", Pool, Every30).Changed.ShouldBeFalse();
        clock.Now += TimeSpan.FromMinutes(30);

        scheduler.Resolve("default", Pool, Every30).Changed.ShouldBeTrue();
    }

    private sealed class SettableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Monitor_slots_never_duplicate_while_the_pool_allows(int monitors)
    {
        for (var seed = 0; seed < 30; seed++)
        {
            _states.Clear();
            var scheduler = Create(seed);
            for (var round = 0; round < 40; round++)
            {
                var result = scheduler.Resolve("default", Pool, Every30, monitors);
                result.Ids.Count.ShouldBe(monitors);
                result.Ids.Distinct().Count().ShouldBe(monitors, $"seed {seed}, round {round}");
                _time.Advance(TimeSpan.FromMinutes(31));
            }
        }
    }

    [Fact]
    public void Monitor_slots_are_stable_between_ticks_and_rotate_together()
    {
        var scheduler = Create();
        var first = scheduler.Resolve("default", Pool, Every30, 2);

        _time.Advance(TimeSpan.FromMinutes(10));
        scheduler.Resolve("default", Pool, Every30, 2).Ids.ShouldBe(first.Ids);

        _time.Advance(TimeSpan.FromMinutes(20));
        var next = scheduler.Resolve("default", Pool, Every30, 2);
        next.Changed.ShouldBeTrue();
        next.Ids.ShouldNotBe(first.Ids);
    }

    [Fact]
    public void Plugging_in_a_monitor_fills_the_new_slot_without_disturbing_existing_ones()
    {
        var scheduler = Create();
        var one = scheduler.Resolve("default", Pool, Every30, 1);

        var two = scheduler.Resolve("default", Pool, Every30, 2);

        two.Ids[0].ShouldBe(one.Ids[0]);
        two.Ids[1].ShouldNotBe(one.Ids[0]);
        scheduler.NextDeadline("default", Every30).ShouldBe(_time.GetUtcNow() + TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Unplugging_a_monitor_keeps_the_primary_slot()
    {
        var scheduler = Create();
        var two = scheduler.Resolve("default", Pool, Every30, 2);

        scheduler.Resolve("default", Pool, Every30, 1).Ids.ShouldBe([two.Ids[0]]);
    }

    [Fact]
    public void State_changed_event_fires_on_mutation_for_persistence()
    {
        var scheduler = Create();
        var count = 0;
        scheduler.StateChanged += () => count++;

        scheduler.Resolve("default", Pool, Every30);
        scheduler.Resolve("default", Pool, Every30);

        count.ShouldBe(1);
    }

    [Fact]
    public void Forget_removes_a_context()
    {
        var scheduler = Create();
        scheduler.Resolve("default#1", Pool, Every30);

        scheduler.Forget("default#1");

        scheduler.Current("default#1").ShouldBeNull();
    }
}
