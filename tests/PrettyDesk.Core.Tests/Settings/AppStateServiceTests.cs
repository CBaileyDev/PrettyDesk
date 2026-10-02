using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Core.Settings;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Settings;

public class AppStateServiceTests
{
    [Theory]
    [InlineData("{\"rotation\":null}")]
    [InlineData("{\"rotation\":{\"default\":null}}")]
    [InlineData("{\"lastApplied\":{\"monitor\":null}}")]
    [InlineData("{\"backup\":{\"monitors\":[null]}}")]
    [InlineData("{\"backup\":{\"monitors\":[{\"backupFile\":\"../outside.jpg\"}]}}")]
    public void Corrupt_state_is_preserved_and_recovers_instead_of_breaking_rotation_or_restore(string json)
    {
        using var dir = new TempDir();
        var path = dir.File("state.json");
        File.WriteAllText(path, json);
        var result = Store(path, new FakeTimeProvider()).Load();

        result.RecoveredFromCorruption.ShouldBeTrue();
        result.Value.Rotation.ShouldBeEmpty();
        File.ReadAllText(result.CorruptBackupPath!).ShouldBe(json);
    }

    private static JsonFileStore<AppState> Store(string path, FakeTimeProvider time) =>
        new(path, AppStateJsonContext.Default.AppState, AppState.CurrentSchemaVersion, time);

    [Fact]
    public void Requested_saves_are_coalesced_into_one_write_after_the_delay()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var path = dir.File("state.json");
        using var service = new AppStateService(Store(path, time), time);

        service.Current.LastApplied["M1"] = "a.png";
        service.RequestSave();
        service.RequestSave();
        File.Exists(path).ShouldBeFalse();

        time.Advance(TimeSpan.FromSeconds(3));

        Store(path, time).Load().Value.LastApplied["M1"].ShouldBe("a.png");
    }

    [Fact]
    public void A_failing_timer_save_does_not_throw_and_is_retried_later()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var path = dir.File("state.json");
        Directory.CreateDirectory(path); // a directory where the file should be: every write fails with an IO error
        using var service = new AppStateService(Store(path, time), time);
        service.Current.LastApplied["M1"] = "a.png";

        service.RequestSave();
        Should.NotThrow(() => time.Advance(TimeSpan.FromSeconds(3)));
        service.LastSaveFailed.ShouldBeTrue();

        Directory.Delete(path);
        time.Advance(TimeSpan.FromSeconds(31));

        service.LastSaveFailed.ShouldBeFalse();
        Store(path, time).Load().Value.LastApplied["M1"].ShouldBe("a.png");
    }

    [Fact]
    public void SaveNow_reports_failure_instead_of_throwing()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var path = dir.File("state.json");
        Directory.CreateDirectory(path);
        using var service = new AppStateService(Store(path, time), time);

        service.SaveNow().ShouldBeFalse();
    }

    [Fact]
    public void Dispose_flushes_pending_state_and_never_throws()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var path = dir.File("state.json");
        var service = new AppStateService(Store(path, time), time);
        service.Current.LastApplied["M1"] = "b.png";
        service.RequestSave();

        service.Dispose();
        Should.NotThrow(service.Dispose);

        Store(path, time).Load().Value.LastApplied["M1"].ShouldBe("b.png");
    }

    [Fact]
    public void A_failed_save_while_disposing_does_not_schedule_another_timer()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var path = dir.File("state.json");
        Directory.CreateDirectory(path);
        var service = new AppStateService(Store(path, time), time);

        Should.NotThrow(service.Dispose);
        Should.NotThrow(() => time.Advance(TimeSpan.FromMinutes(5)));
    }
}
