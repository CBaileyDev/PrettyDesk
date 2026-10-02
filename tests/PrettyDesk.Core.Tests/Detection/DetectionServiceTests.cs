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
        _processes.Running = [Proc(1, "g.exe")];

        service.EvaluateOnce();

        service.Active.ShouldBeNull();
        _processes.Snapshots.ShouldBe(0);
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
}
