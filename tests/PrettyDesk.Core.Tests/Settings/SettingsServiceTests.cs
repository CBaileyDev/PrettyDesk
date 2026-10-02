using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Core.Settings;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Settings;

public class SettingsServiceTests
{
    [Theory]
    [InlineData("{\"customGames\":[null]}")]
    [InlineData("{\"games\":{\"cs2\":null}}")]
    [InlineData("{\"default\":{\"selection\":{\"collections\":[null]}}}")]
    public void Null_collection_entries_are_preserved_in_corrupt_backup(string json)
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, json);
        var service = new SettingsService(new JsonFileStore<AppSettings>(path, SettingsJsonContext.Default.AppSettings,
            AppSettings.CurrentSchemaVersion, new FakeTimeProvider()));

        service.RecoveredFromCorruption.ShouldBeTrue();
        File.ReadAllText(Directory.GetFiles(dir.Path, "settings.corrupt-*.json").Single()).ShouldBe(json);
    }

    [Theory]
    [InlineData("general")]
    [InlineData("detection")]
    [InlineData("default")]
    [InlineData("games")]
    [InlineData("customGames")]
    public void Null_settings_section_is_backed_up_and_recovers_to_defaults(string section)
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, "{\"" + section + "\":null}");
        var service = new SettingsService(new JsonFileStore<AppSettings>(path, SettingsJsonContext.Default.AppSettings,
            AppSettings.CurrentSchemaVersion, new FakeTimeProvider()));

        service.RecoveredFromCorruption.ShouldBeTrue();
        service.Current.General.ShouldNotBeNull();
        service.Current.Detection.ShouldNotBeNull();
        Directory.GetFiles(dir.Path, "settings.corrupt-*.json").ShouldHaveSingleItem();
    }

    [Fact]
    public void A_failed_save_keeps_the_new_value_in_memory_and_still_notifies_listeners()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        Directory.CreateDirectory(path); // every write fails
        var service = new SettingsService(new JsonFileStore<AppSettings>(path, SettingsJsonContext.Default.AppSettings, AppSettings.CurrentSchemaVersion, new FakeTimeProvider()));
        var changed = 0;
        service.Changed += () => changed++;

        Should.NotThrow(() => service.Update(s => s.General.Paused = true));

        service.Current.General.Paused.ShouldBeTrue();
        service.SaveFailed.ShouldBeTrue();
        changed.ShouldBe(1);
    }
}
