using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PrettyDesk.App;
using PrettyDesk.App.Composition;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Detection.Discovery;
using PrettyDesk.Core.Settings;
using PrettyDesk.Core.Tests.Support;
using PrettyDesk.Windows;
using Shouldly;
using Xunit;

namespace PrettyDesk.App.Tests;

/// <summary>Read-only integration of the shipped scanner, catalog and native process source on the current machine.</summary>
public sealed class LiveDetectionTests
{
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    [Fact]
    public async Task Real_installed_manifests_and_process_snapshot_can_be_evaluated_without_modifying_games()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows-only detection probe");
        using var dir = new TempDir();
        await using var provider = new ServiceCollection().AddPrettyDesk(new AppPaths(dir.Path)).BuildServiceProvider();
        var catalog = provider.GetRequiredService<CatalogService>();
        catalog.LoadInitial();
        var installed = provider.GetRequiredService<IInstalledGameScanner>().Scan();
        var matched = InstalledGameScanner.MatchCatalog(catalog.Current, installed);
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steamId = steam?.GetValue("RunningAppID") is int value ? (uint)value : 0;
        var processes = provider.GetRequiredService<ProcessSource>().Snapshot();
        var matcher = new RuleMatcher(GameRegistry.Build(catalog.Current, new AppSettings()), provider.GetRequiredService<ProcessDetailsSource>());
        var running = matcher.Match(processes, steamId);
        catalog.Current.Games.ShouldNotBeEmpty();
        processes.ShouldContain(p => p.Pid == Environment.ProcessId);
        matched.ShouldAllBe(g => catalog.Current.FindGame(g.Id) != null);

        // Only catalog ids and summary counts are persisted when requested. Never write the process snapshot or install paths.
        if (Environment.GetEnvironmentVariable("PRETTYDESK_DETECTION_REPORT") is { Length: > 0 } report)
        {
            var summary = new
            {
                CatalogGames = catalog.Current.Games.Count,
                VerifiedCatalogGames = catalog.Current.Games.Count(g => g.Verified is not null),
                SteamManifests = installed.Count(g => g.Source == "steam"),
                EpicManifests = installed.Count(g => g.Source == "epic"),
                MatchedInstalledIds = matched.Select(g => g.Id).Order().ToArray(),
                RunningGameIds = running.Select(g => g.GameId).Order().ToArray(),
                SnapshotContainsSelf = processes.Any(p => p.Pid == Environment.ProcessId),
                ExistingInstallDirectories = installed.Count(g => Directory.Exists(g.InstallPath)),
                MissingInstallDirectories = installed.Count(g => !Directory.Exists(g.InstallPath)),
                Scope = "Read-only discovery and current running snapshot; no game was launched by this probe.",
            };
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(summary, ReportJson), TestContext.Current.CancellationToken);
        }
    }
}
