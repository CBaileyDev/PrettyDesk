using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Core.Settings;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Settings;

public class JsonFileStoreTests
{
    private static JsonFileStore<AppSettings> Store(string path, FakeTimeProvider? time = null, params ISettingsMigration[] migrations) =>
        new(path, SettingsJsonContext.Default.AppSettings, AppSettings.CurrentSchemaVersion, time ?? new FakeTimeProvider(), migrations);

    [Fact]
    public void Missing_file_yields_defaults_without_recovery()
    {
        using var dir = new TempDir();
        var result = Store(dir.File("settings.json")).Load();

        result.RecoveredFromCorruption.ShouldBeFalse();
        result.Value.General.StartWithWindows.ShouldBeTrue();
        result.Value.Detection.DetectDelaySeconds.ShouldBe(3);
        result.Value.Detection.ExitGraceSeconds.ShouldBe(10);
        result.Value.Default.Interval.ShouldBe(RotationInterval.Every(TimeSpan.FromMinutes(30)));
        result.Value.Default.Selection.Collections.ShouldBe(["default.matte-black"]);
    }

    [Fact]
    public void Round_trip_preserves_values()
    {
        using var dir = new TempDir();
        var store = Store(dir.File("settings.json"));
        var settings = new AppSettings();
        settings.Default.Mode = WallpaperMode.Fixed;
        settings.Default.FixedWallpaperId = "mb-01";
        settings.Default.Interval = RotationInterval.Unlock;
        settings.Games["valorant"] = new GameSettings { Enabled = false, Interval = RotationInterval.Session };
        settings.CustomGames.Add(new CustomGame { Id = "custom-1", DisplayName = "Mine", ExeNames = ["Mine.exe"], Wallpapers = ["user:a.png"] });
        store.Save(settings);

        var loaded = store.Load().Value;

        loaded.Default.Mode.ShouldBe(WallpaperMode.Fixed);
        loaded.Default.FixedWallpaperId.ShouldBe("mb-01");
        loaded.Default.Interval.Kind.ShouldBe(RotationIntervalKind.Unlock);
        loaded.Games["valorant"].Enabled.ShouldBeFalse();
        loaded.CustomGames.ShouldHaveSingleItem().ExeNames.ShouldBe(["Mine.exe"]);
    }

    [Fact]
    public void Written_file_uses_the_documented_wire_format()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        Store(path).Save(new AppSettings());

        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        json["schemaVersion"]!.GetValue<int>().ShouldBe(1);
        json["default"]!["mode"]!.GetValue<string>().ShouldBe("rotate");
        json["default"]!["interval"]!.GetValue<string>().ShouldBe("PT30M");
        json["default"]!["order"]!.GetValue<string>().ShouldBe("shuffle");
        json["monitors"]!["mode"]!.GetValue<string>().ShouldBe("same");
    }

    [Fact]
    public void Unknown_fields_are_preserved_at_every_level()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, """
            { "schemaVersion": 1, "futureTopLevel": {"a": 1},
              "general": { "startWithWindows": false, "futureFlag": true },
              "games": { "cs2": { "enabled": false, "futureGameField": "x" } } }
            """);
        var store = Store(path);

        var loaded = store.Load().Value;
        store.Save(loaded);
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        loaded.General.StartWithWindows.ShouldBeFalse();
        json["futureTopLevel"]!["a"]!.GetValue<int>().ShouldBe(1);
        json["general"]!["futureFlag"]!.GetValue<bool>().ShouldBeTrue();
        json["games"]!["cs2"]!["futureGameField"]!.GetValue<string>().ShouldBe("x");
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{ "schemaVersion": 1, "default": { "mode": "sideways" } }""")]
    public void Corrupt_file_is_backed_up_and_replaced_with_defaults(string content)
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, content);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        var result = Store(path, time).Load();

        result.RecoveredFromCorruption.ShouldBeTrue();
        result.Value.Default.Mode.ShouldBe(WallpaperMode.Rotate);
        result.CorruptBackupPath.ShouldNotBeNull();
        Path.GetFileName(result.CorruptBackupPath).ShouldStartWith("settings.corrupt-20261001T120000");
        File.ReadAllText(result.CorruptBackupPath).ShouldBe(content);
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public void Migrations_run_in_order_and_keep_unknown_fields()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 0, "startWithWindows": false, "keep": 7 }""");
        var migration = new LambdaMigration(0, doc =>
        {
            doc["general"] = new JsonObject { ["startWithWindows"] = doc["startWithWindows"]!.GetValue<bool>() };
            doc.Remove("startWithWindows");
        });

        var loaded = Store(path, null, migration).Load();

        loaded.RecoveredFromCorruption.ShouldBeFalse();
        loaded.Value.SchemaVersion.ShouldBe(1);
        loaded.Value.General.StartWithWindows.ShouldBeFalse();
        loaded.Value.Extra!["keep"].GetInt32().ShouldBe(7);
    }

    [Fact]
    public void Missing_migration_step_is_treated_as_corrupt()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 0 }""");

        Store(path).Load().RecoveredFromCorruption.ShouldBeTrue();
    }

    [Fact]
    public void Save_is_atomic_and_leaves_no_temp_file()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var store = Store(path);
        store.Save(new AppSettings());
        store.Save(new AppSettings());

        Directory.GetFiles(dir.Path).Select(Path.GetFileName).ShouldBe(["settings.json"]);
    }

    private sealed class LambdaMigration(int from, Action<JsonObject> apply) : ISettingsMigration
    {
        public int FromVersion => from;

        public void Migrate(JsonObject document) => apply(document);
    }
}
