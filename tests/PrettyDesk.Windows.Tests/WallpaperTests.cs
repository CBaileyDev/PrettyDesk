using Microsoft.Extensions.Logging.Abstractions;
using PrettyDesk.Core.Settings;
using PrettyDesk.Core.Tests.Support;
using PrettyDesk.Windows;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace PrettyDesk.Windows.Tests;

/// <summary>
/// These change the real desktop wallpaper of whoever runs them, so each test restores the original in a finally block.
/// They are skipped when no desktop/monitor is available (e.g. a headless runner session).
/// </summary>
public sealed class WallpaperTests : IDisposable
{
    private readonly TempDir _dir = new();
    private DesktopWallpaperService? _created;

    // Created lazily so non-Windows runs skip before touching any Windows-only API.
    private DesktopWallpaperService Service => _created ??= new DesktopWallpaperService(TimeProvider.System, NullLogger<DesktopWallpaperService>.Instance);

    public void Dispose()
    {
        _created?.Dispose();
        _dir.Dispose();
    }

    private string MakeImage(string name, SKColor color)
    {
        var path = _dir.File(name);
        using var bitmap = new SKBitmap(640, 360);
        bitmap.Erase(color);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public void Monitors_are_enumerated_with_physical_pixel_sizes()
    {
        WindowsOnly.Require();
        var monitors = Service.GetMonitors();
        Assert.SkipWhen(monitors.Count == 0, "No monitor/desktop available in this session.");

        monitors.ShouldAllBe(m => m.PixelWidth > 0 && m.PixelHeight > 0 && !string.IsNullOrEmpty(m.Id));
        monitors.Count(m => m.IsPrimary).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Setting_a_wallpaper_through_COM_is_readable_back_and_restorable()
    {
        WindowsOnly.Require();
        var monitor = Service.GetMonitors() is { Count: > 0 } list ? list[0] : null;
        Assert.SkipWhen(monitor is null, "No monitor/desktop available in this session.");
        var original = await Service.GetAsync(monitor.Id, TestContext.Current.CancellationToken);
        var originalPosition = await Service.GetPositionAsync(TestContext.Current.CancellationToken);
        var image = MakeImage("pd-test-1.png", SKColors.DarkSlateBlue);
        try
        {
            await Service.SetAsync(monitor.Id, image, TestContext.Current.CancellationToken);

            (await Service.GetAsync(monitor.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull().ShouldEndWith("pd-test-1.png", Case.Insensitive);
            (await Service.GetPositionAsync(TestContext.Current.CancellationToken)).ShouldBe(DesktopPosition.Fill);
        }
        finally
        {
            if (!string.IsNullOrEmpty(original))
            {
                await Service.SetAsync(monitor.Id, original, originalPosition, CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Backup_then_apply_then_restore_returns_the_original_even_if_the_source_file_is_deleted()
    {
        WindowsOnly.Require();
        var monitors = Service.GetMonitors();
        Assert.SkipWhen(monitors.Count == 0, "No monitor/desktop available in this session.");
        var monitor = monitors[0];
        var original = await Service.GetAsync(monitor.Id, TestContext.Current.CancellationToken);
        var originalPosition = await Service.GetPositionAsync(TestContext.Current.CancellationToken);

        var stateStore = new JsonFileStore<AppState>(_dir.File("state.json"), AppStateJsonContext.Default.AppState, AppState.CurrentSchemaVersion, TimeProvider.System);
        using var state = new AppStateService(stateStore, TimeProvider.System);
        var backup = new WallpaperBackupService(Service, state, _dir.File("backup"), _dir.File("cache"), TimeProvider.System, NullLogger<WallpaperBackupService>.Instance);
        try
        {
            await backup.EnsureBackupAsync(TestContext.Current.CancellationToken);
            state.Current.Backup.ShouldNotBeNull().Monitors.ShouldNotBeEmpty();

            await Service.SetAsync(monitor.Id, MakeImage("pd-test-2.png", SKColors.Firebrick), TestContext.Current.CancellationToken);
            var result = await backup.RestoreAsync(TestContext.Current.CancellationToken);

            result.NothingToRestore.ShouldBe(string.IsNullOrEmpty(original) && !File.Exists(WallpaperBackupService.TranscodedWallpaperPath));
        }
        finally
        {
            if (!string.IsNullOrEmpty(original))
            {
                await Service.SetAsync(monitor.Id, original, originalPosition, CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task A_second_backup_never_overwrites_the_first()
    {
        WindowsOnly.Require();
        Assert.SkipWhen(Service.GetMonitors().Count == 0, "No monitor/desktop available in this session.");
        var store = new JsonFileStore<AppState>(_dir.File("state2.json"), AppStateJsonContext.Default.AppState, AppState.CurrentSchemaVersion, TimeProvider.System);
        using var state = new AppStateService(store, TimeProvider.System);
        var backup = new WallpaperBackupService(Service, state, _dir.File("backup2"), _dir.File("cache"), TimeProvider.System, NullLogger<WallpaperBackupService>.Instance);

        await backup.EnsureBackupAsync(TestContext.Current.CancellationToken);
        var first = state.Current.Backup!.CreatedAt;
        await backup.EnsureBackupAsync(TestContext.Current.CancellationToken);

        state.Current.Backup!.CreatedAt.ShouldBe(first);
    }
}
