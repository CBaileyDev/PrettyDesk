#if PRETTYDESK_ACCEPTANCE
using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PrettyDesk.App.Composition;
using PrettyDesk.App.Services;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Imaging;
using PrettyDesk.Windows;
using Velopack;

namespace PrettyDesk.App;

// Compiled exclusively into isolated acceptance packages, never into release builds.
internal static class AcceptanceHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static string DataRoot => typeof(AcceptanceHarness).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "AcceptanceDataRoot").Value
        ?? throw new InvalidOperationException("AcceptanceDataRoot must be configured.");

    public static void RecordTrayReady(double milliseconds) => Write("tray-ready.json", new
    {
        AppConstants.DisplayVersion,
        StartupMilliseconds = milliseconds,
        ProcessId = Environment.ProcessId,
    });

    public static async Task<int> RunAsync(string[] args)
    {
        if (args[0] == "--acceptance-hold")
        {
            var app = new System.Windows.Application();
            app.Run(new System.Windows.Window
            {
                Title = "PrettyDesk latency fixture (automated test)",
                Width = 360,
                Height = 120,
                Content = "Synthetic game process. Closes automatically after detection.",
            });
            return 0;
        }

        var paths = new AppPaths();
        paths.EnsureCreated();
        using var provider = new ServiceCollection().AddPrettyDesk(paths).BuildServiceProvider();
        var wallpaper = provider.GetRequiredService<DesktopWallpaperService>();
        try
        {
            switch (args[0])
            {
                case "--acceptance-version":
                    Write("version.json", new { AppConstants.DisplayVersion });
                    break;
                case "--acceptance-update":
                    var settings = provider.GetRequiredService<ISettingsProvider>();
                    var backend = new VelopackUpdateBackend(settings, _ => new UpdateManager(args[1],
                        new UpdateOptions { ExplicitChannel = "win-x64-stable" }));
                    if (!backend.IsSupported)
                    {
                        throw new InvalidOperationException("Update verification requires an installed copy.");
                    }

                    var update = await backend.CheckAsync(CancellationToken.None)
                        ?? throw new InvalidOperationException("The test feed did not offer a newer release.");
                    await backend.DownloadAsync(update, _ => { }, CancellationToken.None);
                    Write("update-downloaded.json", new { update.Version });
                    backend.ApplyAndRestart(update);
                    break;
                case "--acceptance-backup-apply":
                    await provider.GetRequiredService<WallpaperBackupService>().EnsureBackupAsync();
                    foreach (var monitor in wallpaper.GetMonitors())
                    {
                        await wallpaper.SetAsync(monitor.Id, args[1]);
                    }

                    Write("backup-applied.json", new { Applied = true });
                    break;
                case "--acceptance-desktop":
                    var monitors = new List<object>();
                    foreach (var monitor in wallpaper.GetMonitors())
                    {
                        monitors.Add(new { monitor.Id, monitor.PixelWidth, monitor.PixelHeight, Path = await wallpaper.GetAsync(monitor.Id) });
                    }

                    Write(args[1], new { Monitors = monitors, Position = await wallpaper.GetPositionAsync(), Color = await wallpaper.GetBackgroundColorAsync(), Type = WindowsSystemState.ReadBackgroundType() });
                    break;
                case "--acceptance-restore-check":
                    var saved = provider.GetRequiredService<Core.Settings.AppStateService>().Current.Backup
                        ?? throw new InvalidOperationException("No saved backup exists for this acceptance run.");
                    var typeMatches = WindowsSystemState.ReadBackgroundType() == saved.BackgroundType;
                    var positionMatches = (int)await wallpaper.GetPositionAsync() == saved.Position;
                    var colorMatches = await wallpaper.GetBackgroundColorAsync() == saved.BackgroundColor;
                    var slideshowMatches = true;
                    if (saved.BackgroundType == "slideshow")
                    {
                        var restored = await wallpaper.GetSlideshowAsync();
                        slideshowMatches = restored.Items.SequenceEqual(saved.SlideshowItems) && restored.Options == saved.SlideshowOptions && restored.Interval == saved.SlideshowInterval;
                    }

                    Write("restore-check.json", new { TypeMatches = typeMatches, PositionMatches = positionMatches, ColorMatches = colorMatches, SlideshowMatches = slideshowMatches });
                    if (!typeMatches || !positionMatches || !colorMatches || !slideshowMatches)
                    {
                        throw new InvalidOperationException("The desktop no longer matches the saved original wallpaper configuration.");
                    }

                    break;
                case "--acceptance-switch-benchmark":
                    await BenchmarkSwitchesAsync(provider, wallpaper, args[1]);
                    break;
                default:
                    throw new ArgumentException("Unknown acceptance command.", nameof(args));
            }

            return 0;
        }
        finally
        {
            wallpaper.Dispose();
        }
    }

    private static async Task BenchmarkSwitchesAsync(ServiceProvider provider, DesktopWallpaperService wallpaper, string fixtureExe)
    {
        var catalog = provider.GetRequiredService<CatalogService>();
        catalog.LoadInitial();
        var content = provider.GetRequiredService<ContentLibrary>();
        var renderer = provider.GetRequiredService<WallpaperRenderer>();
        var monitor = wallpaper.GetMonitors()[0];
        foreach (var id in new[] { "mb-01", "ar-01" })
        {
            await renderer.RenderAsync(content.TryGetAsset(id) ?? throw new InvalidOperationException("Missing bundled fixture art."), monitor);
        }

        var samples = new List<object>();
        for (var i = 0; i < 5; i++)
        {
            await WaitForWallpaperAsync(wallpaper, monitor.Id, "mb-01");
            var timer = Stopwatch.StartNew();
            using var fixture = Process.Start(new ProcessStartInfo(fixtureExe, "--acceptance-hold") { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Fixture launch failed.");
            try
            {
                await WaitForWallpaperAsync(wallpaper, monitor.Id, "ar-01");
                var enter = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                fixture.Kill();
                await fixture.WaitForExitAsync();
                await WaitForWallpaperAsync(wallpaper, monitor.Id, "mb-01");
                samples.Add(new { EnterMilliseconds = enter, ExitMilliseconds = timer.Elapsed.TotalMilliseconds });
            }
            finally
            {
                if (!fixture.HasExited)
                {
                    fixture.Kill();
                }
            }
        }

        Write("switching.json", new { Fixture = "synthetic custom game process", Cached = true, Samples = samples });
    }

    private static async Task WaitForWallpaperAsync(DesktopWallpaperService wallpaper, string monitorId, string id)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(45))
        {
            if ((await wallpaper.GetAsync(monitorId))?.Contains(id + "_", StringComparison.Ordinal) == true)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Expected wallpaper was not applied: " + id);
    }

    private static void Write(string name, object value)
    {
        Directory.CreateDirectory(DataRoot);
        File.WriteAllText(Path.Combine(DataRoot, name), JsonSerializer.Serialize(value, JsonOptions));
    }
}
#endif
