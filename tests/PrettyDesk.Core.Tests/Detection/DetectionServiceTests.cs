using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Detection;
using Shouldly;
using Xunit;
using static PrettyDesk.Core.Tests.Support.TestData;

namespace PrettyDesk.Core.Tests.Detection;

public class DetectionServiceTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeProcesses _processes = new();
    private readonly FakeForeground _foreground = new();

    private DetectionService Create(bool enabled = true, FakeSteam? steam = null)
    {
        var registry = Registry([Game("g", configure: b => { b.Exe.Add("g.exe"); b.Steam.Add(5); })]);
        var config = new DetectionConfiguration(enabled, TimeSpan.FromSeconds(2), DetectionOptions.Default, new RuleMatcher(registry, null));
        return new DetectionService(_processes, _foreground, steam, config, _time, NullLogger<DetectionService>.Instance);
    }

    [Fact]
    public async Task Evaluate_drives_debounce_and_grace_with_the_injected_clock()
    {
        await using var service = Create();
        var changes = new List<ActiveGameChange>();
        service.ActiveChanged += changes.Add;
        _processes.Running = [Proc(42, "g.exe")];

        service.EvaluateOnce();
        _time.Advance(TimeSpan.FromSeconds(3));
        service.EvaluateOnce();
        service.Active!.GameId.ShouldBe("g");

        _processes.Running = [];
        service.EvaluateOnce();
        service.Active!.InGrace.ShouldBeTrue();
        _time.Advance(TimeSpan.FromSeconds(10));
        service.EvaluateOnce();

        service.Active.ShouldBeNull();
        changes.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Disposing_twice_is_harmless_because_the_container_may_dispose_a_service_once_per_alias()
    {
        var service = Create();
        service.Start();

        await service.DisposeAsync();

        await Should.NotThrowAsync(async () => await service.DisposeAsync());
    }

    [Fact]
    public async Task Disabled_detection_clears_everything_and_does_not_snapshot()
    {
        await using var service = Create(enabled: false);
        service.EvaluateOnce();

        service.Active.ShouldBeNull();
        _processes.Snapshots.ShouldBe(0);

        // A game can start while detection is disabled, so enabling it must take a fresh snapshot and match.
        _processes.Running = [Proc(1, "g.exe")];
        var registry = Registry([Game("g", configure: b => b.Exe.Add("g.exe"))]);
        service.Reconfigure(new DetectionConfiguration(true, TimeSpan.FromSeconds(2), DetectionOptions.Default, new RuleMatcher(registry, null)));
        service.EvaluateOnce();

        _processes.Snapshots.ShouldBe(1);
        service.BroadEvaluationCount.ShouldBe(1);
    }

    [Fact]
    public async Task Observation_reports_foreground_exe_and_matched_ids()
    {
        await using var service = Create();
        DetectionObservation? seen = null;
        service.Observed += o => seen = o;
        _processes.Running = [Proc(7, "g.exe")];
        _foreground.Current = new ForegroundInfo(7, "g.exe");

        service.EvaluateOnce();

        seen.ShouldNotBeNull().ForegroundExe.ShouldBe("g.exe");
        seen.MatchedGameIds.ShouldBe(["g"]);
    }

    [Fact]
    public async Task Poll_timer_and_deadline_timer_activate_the_game_without_manual_evaluation()
    {
        await using var service = Create();
        var activated = new TaskCompletionSource<ActiveGameChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ActiveChanged += c => activated.TrySetResult(c);
        _processes.Running = [Proc(42, "g.exe")];
        service.Start();

        // Poll fires at t=0 and every 2 s; debounce completes at the 3 s deadline.
        for (var i = 0; i < 10 && !activated.Task.IsCompleted; i++)
        {
            await WaitForIdleAsync(service);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        var change = await activated.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        change.Current!.GameId.ShouldBe("g");
    }

    [Fact]
    public async Task Foreground_change_triggers_an_immediate_evaluation()
    {
        await using var service = Create();
        service.Start();
        await WaitForIdleAsync(service);
        _processes.Running = [Proc(9, "g.exe")];
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Observed += o =>
        {
            if (o.MatchedGameIds.Count > 0)
            {
                observed.TrySetResult();
            }
        };

        _foreground.RaiseChanged();

        await observed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Steam_app_id_change_triggers_evaluation()
    {
        var steam = new FakeSteam();
        await using var service = Create(steam: steam);
        service.Start();
        await WaitForIdleAsync(service);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Observed += o =>
        {
            if (o.MatchedGameIds.Contains("g"))
            {
                observed.TrySetResult();
            }
        };

        steam.CurrentAppId = 5;
        steam.RaiseChanged();

        await observed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_throwing_process_source_does_not_kill_the_loop()
    {
        await using var service = Create();
        service.Start();
        await WaitForIdleAsync(service);
        _processes.Throw = true;
        _foreground.RaiseChanged();
        await WaitForIdleAsync(service);
        _processes.Throw = false;
        _processes.Running = [Proc(1, "g.exe")];
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Observed += o =>
        {
            if (o.MatchedGameIds.Count > 0)
            {
                observed.TrySetResult();
            }
        };

        _foreground.RaiseChanged();

        await observed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    /// <summary>Lets the consumer loop drain pending signals (a real yield, no timing assumptions about the fake clock).</summary>
    private static async Task WaitForIdleAsync(DetectionService service)
    {
        _ = service;
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
        }

        await Task.Run(() => Thread.Yield());
    }

    [Fact]
    public async Task Unchanged_session_reuses_matching_but_keeps_start_exit_and_foreground_checks()
    {
        await using var service = Create();
        _processes.Running = [Proc(42, "g.exe")];
        service.EvaluateOnce();
        _time.Advance(TimeSpan.FromSeconds(3));
        service.EvaluateOnce();
        var broad = service.BroadEvaluationCount;
        for (var i = 0; i < 10; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(2));
            service.EvaluateOnce();
        }

        service.BroadEvaluationCount.ShouldBe(broad);
        _processes.Snapshots.ShouldBe(12);
        _foreground.Current = new ForegroundInfo(42, "g.exe");
        service.EvaluateOnce();
        service.BroadEvaluationCount.ShouldBe(broad + 1);
        _processes.Running = [];
        service.EvaluateOnce();
        service.Active!.InGrace.ShouldBeTrue();
        _time.Advance(TimeSpan.FromSeconds(10));
        service.EvaluateOnce();
        service.Active.ShouldBeNull();
    }

    [Fact]
    public async Task Unchanged_name_only_matches_are_reused_while_debounce_advances()
    {
        await using var service = Create();
        _processes.Running = [Proc(42, "g.exe")];

        service.EvaluateOnce();
        service.BroadEvaluationCount.ShouldBe(1);
        service.Active.ShouldBeNull();

        _time.Advance(TimeSpan.FromSeconds(2));
        service.EvaluateOnce();
        service.BroadEvaluationCount.ShouldBe(1);
        service.Active.ShouldBeNull("the game still has one second of its configured debounce");

        _time.Advance(TimeSpan.FromSeconds(1));
        service.EvaluateOnce();

        service.BroadEvaluationCount.ShouldBe(1);
        service.Active!.GameId.ShouldBe("g");
        _processes.Snapshots.ShouldBe(3, "process enumeration and tracker observation must continue on every poll");
    }

    [Fact]
    public async Task Stable_idle_snapshot_rematches_at_the_thirty_second_fallback()
    {
        await using var service = Create();
        _processes.Running = [Proc(88, "unrelated.exe")];

        // Simulate ten minutes of two-second polls without sleeping or depending on wall-clock scheduling.
        for (var poll = 0; poll <= 300; poll++)
        {
            service.EvaluateOnce();
            _time.Advance(TimeSpan.FromSeconds(2));
        }

        service.BroadEvaluationCount.ShouldBe(21, "the initial scan plus the 30-second safety refreshes");
        _processes.Snapshots.ShouldBe(301, "the regular process-enumeration cadence must remain unchanged");
    }

    [Fact]
    public async Task Inactive_match_fallback_uses_monotonic_time_across_a_forward_wall_clock_change()
    {
        var time = new AdjustableTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var registry = Registry([Game("g", configure: b => b.Exe.Add("g.exe"))]);
        var config = new DetectionConfiguration(true, TimeSpan.FromSeconds(2), DetectionOptions.Default, new RuleMatcher(registry, null));
        await using var service = new DetectionService(_processes, _foreground, null, config, time, NullLogger<DetectionService>.Instance);
        _processes.Running = [Proc(88, "unrelated.exe")];
        service.EvaluateOnce();
        service.BroadEvaluationCount.ShouldBe(1);

        time.Advance(TimeSpan.FromSeconds(15));
        time.SetUtcNow(time.GetUtcNow().AddHours(1));
        service.EvaluateOnce();
        service.BroadEvaluationCount.ShouldBe(1, "a wall-clock jump must not expire the interval early");

        time.Advance(TimeSpan.FromSeconds(15));
        service.EvaluateOnce();

        service.BroadEvaluationCount.ShouldBe(2, "the safety refresh follows elapsed time, even if the wall clock jumps forward");
    }

    [Fact]
    public async Task Inactive_match_cache_is_invalidated_by_snapshot_foreground_steam_and_matcher_changes()
    {
        var steam = new FakeSteam();
        await using var service = Create(steam: steam);
        _processes.Running = [Proc(42, "g.exe")];

        service.EvaluateOnce();
        service.BroadEvaluationCount.ShouldBe(1);

        _foreground.Current = new ForegroundInfo(42, "g.exe");
        service.EvaluateOnce();
        service.BroadEvaluationCount.ShouldBe(2);

        steam.CurrentAppId = 5;
        service.EvaluateOnce();
        service.BroadEvaluationCount.ShouldBe(3);

        _processes.Running = [new ProcessInfo(42, "g.exe", 1, _time.GetUtcNow())];
        service.EvaluateOnce();
        service.BroadEvaluationCount.ShouldBe(4);

        var registry = Registry([Game("g", configure: b => { b.Exe.Add("g.exe"); b.Steam.Add(5); })]);
        service.Reconfigure(new DetectionConfiguration(true, TimeSpan.FromSeconds(2), DetectionOptions.Default, new RuleMatcher(registry, null)));
        service.EvaluateOnce();

        service.BroadEvaluationCount.ShouldBe(5);
        service.Active.ShouldBeNull("the invalidations happen before the detect-delay deadline");
    }

    [Fact]
    public async Task Live_detail_rules_are_rematched_on_each_poll()
    {
        var details = new FakeDetails();
        details.Paths[42] = @"C:\Games\PrettyDeskGame\game.exe";
        var registry = Registry([Game("g", configure: b => { b.Exe.Add("g.exe"); b.Path.Add("PrettyDeskGame"); })]);
        var config = new DetectionConfiguration(true, TimeSpan.FromSeconds(2), DetectionOptions.Default, new RuleMatcher(registry, details));
        await using var service = new DetectionService(_processes, _foreground, null, config, _time, NullLogger<DetectionService>.Instance);
        _processes.Running = [Proc(42, "g.exe")];

        service.EvaluateOnce();
        _time.Advance(TimeSpan.FromSeconds(1));
        service.EvaluateOnce();

        service.BroadEvaluationCount.ShouldBe(2);
        details.PathCalls.ShouldBe(2);
        service.Active.ShouldBeNull();
    }

    [Fact]
    public async Task Reordering_an_unchanged_process_snapshot_does_not_repeat_broad_matching()
    {
        await using var service = Create();
        var game = Proc(42, "g.exe");
        var background = Proc(7, "background.exe");
        _processes.Running = [game, background];
        service.EvaluateOnce();
        _time.Advance(TimeSpan.FromSeconds(3));
        service.EvaluateOnce();
        var broad = service.BroadEvaluationCount;

        _processes.Running = [background, game];
        service.EvaluateOnce();

        service.BroadEvaluationCount.ShouldBe(broad);
        service.Active!.GameId.ShouldBe("g");
    }

    [Fact]
    public async Task Changed_process_identity_and_fallback_expiry_force_matching()
    {
        await using var service = Create();
        _processes.Running = [new ProcessInfo(42, "g.exe", 1, _time.GetUtcNow())];
        service.EvaluateOnce();
        _time.Advance(TimeSpan.FromSeconds(3));
        service.EvaluateOnce();
        var broad = service.BroadEvaluationCount;
        _time.Advance(TimeSpan.FromSeconds(30));
        service.EvaluateOnce();
        service.BroadEvaluationCount.ShouldBe(broad + 1);
        _processes.Running = [new ProcessInfo(42, "g.exe", 1, _time.GetUtcNow())];
        service.EvaluateOnce();
        service.BroadEvaluationCount.ShouldBe(broad + 2);
    }

    private sealed class FakeProcesses : IProcessSource
    {
        public IReadOnlyList<ProcessInfo> Running { get; set; } = [];
        public int Snapshots { get; private set; }
        public bool Throw { get; set; }

        public IReadOnlyList<ProcessInfo> Snapshot()
        {
            Snapshots++;
            return Throw ? throw new InvalidOperationException("boom") : Running;
        }
    }

    private sealed class FakeForeground : IForegroundSource
    {
        public ForegroundInfo? Current { get; set; }

        public event Action? Changed;

        public void RaiseChanged() => Changed?.Invoke();
    }

    private sealed class FakeSteam : ISteamRunningAppSource
    {
        public uint CurrentAppId { get; set; }

        public event Action? Changed;

        public void RaiseChanged() => Changed?.Invoke();
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan amount)
        {
            _utcNow += amount;
            _timestamp += amount.Ticks;
        }

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }
}
