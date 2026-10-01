using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Presentation.Services;
using Shouldly;
using Xunit;

namespace PrettyDesk.Presentation.Tests.ViewModels;

public sealed class UpdateCoordinatorTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeBackend _backend = new();
    private UpdateCoordinator? _coordinator;

    public void Dispose()
    {
        _coordinator?.Dispose();
        _backend.Dispose();
    }

    private UpdateCoordinator Create()
    {
        _coordinator = new UpdateCoordinator(_backend, _time, NullLogger<UpdateCoordinator>.Instance);
        return _coordinator;
    }

    [Fact]
    public async Task Portable_and_dev_builds_report_not_supported_and_never_touch_the_backend()
    {
        _backend.Supported = false;
        var updates = Create();

        updates.State.Kind.ShouldBe(UpdateStateKind.NotSupported);
        updates.Start();
        _time.Advance(TimeSpan.FromDays(2));
        await updates.CheckAsync(TestContext.Current.CancellationToken);

        _backend.Checks.ShouldBe(0);
    }

    [Fact]
    public async Task Up_to_date_when_the_feed_has_nothing_newer()
    {
        var updates = Create();

        await updates.CheckAsync(TestContext.Current.CancellationToken);

        updates.State.Kind.ShouldBe(UpdateStateKind.UpToDate);
    }

    [Fact]
    public async Task A_newer_release_is_downloaded_in_the_background_and_reported_with_progress()
    {
        _backend.Next = new AvailableUpdate("1.2.0", "token");
        _backend.ProgressSteps = [10, 10, 55, 100];
        var seen = new List<UpdateState>();
        var updates = Create();
        updates.Changed += () => seen.Add(updates.State);

        await updates.CheckAsync(TestContext.Current.CancellationToken);

        seen.Select(s => s.Kind).ShouldBe(
        [
            UpdateStateKind.Checking, UpdateStateKind.Available, UpdateStateKind.Downloading, UpdateStateKind.Downloading,
            UpdateStateKind.Downloading, UpdateStateKind.Downloading, UpdateStateKind.ReadyToInstall,
        ]);
        seen.Where(s => s.Kind == UpdateStateKind.Downloading).Select(s => s.Percent).ShouldBe([0, 10, 55, 100], "duplicate percentages are coalesced");
        updates.State.ShouldBe(new UpdateState(UpdateStateKind.ReadyToInstall, "1.2.0"));
        _backend.Downloaded.ShouldBe(["1.2.0"]);
    }

    [Fact]
    public async Task Apply_restarts_into_the_downloaded_release_only_when_it_is_ready()
    {
        var updates = Create();
        updates.ApplyAndRestart();
        _backend.Applied.ShouldBeEmpty();

        _backend.Next = new AvailableUpdate("1.2.0", "token");
        await updates.CheckAsync(TestContext.Current.CancellationToken);
        updates.ApplyAndRestart();

        _backend.Applied.ShouldBe(["1.2.0"]);
    }

    [Fact]
    public async Task A_release_downloaded_in_an_earlier_session_is_ready_immediately_and_not_fetched_again()
    {
        _backend.Pending = new AvailableUpdate("1.1.0", "old");
        _backend.Next = new AvailableUpdate("1.2.0", "newer");
        var updates = Create();

        updates.State.ShouldBe(new UpdateState(UpdateStateKind.ReadyToInstall, "1.1.0"));
        await updates.CheckAsync(TestContext.Current.CancellationToken);

        _backend.Checks.ShouldBe(0);
        updates.ApplyAndRestart();
        _backend.Applied.ShouldBe(["1.1.0"]);
    }

    [Fact]
    public async Task Failures_are_quiet_and_the_next_check_recovers()
    {
        _backend.Throw = new HttpRequestException("offline");
        var updates = Create();

        await updates.CheckAsync(TestContext.Current.CancellationToken);
        updates.State.Kind.ShouldBe(UpdateStateKind.Failed);

        _backend.Throw = null;
        await updates.CheckAsync(TestContext.Current.CancellationToken);
        updates.State.Kind.ShouldBe(UpdateStateKind.UpToDate);
    }

    [Fact]
    public async Task A_failed_download_leaves_nothing_ready_to_install()
    {
        _backend.Next = new AvailableUpdate("1.2.0", "token");
        _backend.ThrowOnDownload = new IOException("disk full");
        var updates = Create();

        await updates.CheckAsync(TestContext.Current.CancellationToken);

        updates.State.Kind.ShouldBe(UpdateStateKind.Failed);
        updates.ApplyAndRestart();
        _backend.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_during_the_download_returns_to_idle_instead_of_reporting_a_failure()
    {
        _backend.Next = new AvailableUpdate("1.2.0", "token");
        using var cts = new CancellationTokenSource();
        _backend.ThrowOnDownload = new OperationCanceledException(cts.Token);
        _backend.OnDownload = () => cts.Cancel();
        var updates = Create();

        await updates.CheckAsync(cts.Token);

        updates.State.Kind.ShouldBe(UpdateStateKind.Idle);
    }

    [Fact]
    public async Task A_check_cancelled_before_it_starts_throws_to_the_caller()
    {
        var updates = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => updates.CheckAsync(cts.Token));
    }

    [Fact]
    public async Task The_schedule_checks_after_the_startup_delay_and_then_every_twelve_hours()
    {
        var updates = Create();
        updates.Start();

        _time.Advance(TimeSpan.FromSeconds(29));
        _backend.Checks.ShouldBe(0);

        _time.Advance(TimeSpan.FromSeconds(2));
        await _backend.WaitForChecksAsync(1);

        _time.Advance(TimeSpan.FromHours(11));
        _backend.Checks.ShouldBe(1);
        _time.Advance(TimeSpan.FromHours(1));
        await _backend.WaitForChecksAsync(2);
    }

    [Fact]
    public async Task Concurrent_checks_do_not_overlap()
    {
        var gate = new TaskCompletionSource();
        _backend.Block = gate.Task;
        var updates = Create();

        var first = updates.CheckAsync(TestContext.Current.CancellationToken);
        var second = updates.CheckAsync(TestContext.Current.CancellationToken);
        _backend.Checks.ShouldBe(1);
        gate.SetResult();
        await Task.WhenAll(first, second);

        _backend.Checks.ShouldBe(2, "the second check ran after the first finished, never in parallel");
        _backend.MaxParallel.ShouldBe(1);
    }

    private sealed class FakeBackend : IUpdateBackend, IDisposable
    {
        private int _parallel;
        private readonly SemaphoreSlim _checkSignal = new(0);

        public bool Supported { get; set; } = true;
        public AvailableUpdate? Pending { get; set; }
        public AvailableUpdate? Next { get; set; }
        public Exception? Throw { get; set; }
        public Exception? ThrowOnDownload { get; set; }
        public Action? OnDownload { get; set; }
        public int[] ProgressSteps { get; set; } = [];
        public Task? Block { get; set; }
        public int Checks { get; private set; }
        public int MaxParallel { get; private set; }
        public List<string> Downloaded { get; } = [];
        public List<string> Applied { get; } = [];

        public bool IsSupported => Supported;

        public AvailableUpdate? PendingRestart => Pending;

        public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
        {
            Checks++;
            MaxParallel = Math.Max(MaxParallel, Interlocked.Increment(ref _parallel));
            try
            {
                if (Block is not null)
                {
                    await Block;
                }

                if (Throw is not null)
                {
                    throw Throw;
                }

                return Next;
            }
            finally
            {
                Interlocked.Decrement(ref _parallel);
                _checkSignal.Release();
            }
        }

        public void Dispose() => _checkSignal.Dispose();

        public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellationToken)
        {
            OnDownload?.Invoke();
            if (ThrowOnDownload is not null)
            {
                throw ThrowOnDownload;
            }

            foreach (var step in ProgressSteps)
            {
                progress(step);
            }

            Downloaded.Add(update.Version);
            return Task.CompletedTask;
        }

        public void ApplyAndRestart(AvailableUpdate update) => Applied.Add(update.Version);

        public async Task WaitForChecksAsync(int count)
        {
            while (Checks < count)
            {
                await _checkSignal.WaitAsync(TestContext.Current.CancellationToken);
            }
        }
    }
}
