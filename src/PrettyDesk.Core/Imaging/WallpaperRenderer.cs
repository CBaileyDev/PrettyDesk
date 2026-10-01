using Microsoft.Extensions.Logging;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using SkiaSharp;

namespace PrettyDesk.Core.Imaging;

/// <summary>
/// Turns <c>(wallpaper, monitor)</c> into an exact-pixel PNG (FR-APPLY-2): pick the closest-ratio variant, focal-crop the
/// small remaining difference, resample, and write a PNG (Windows re-encodes JPEG wallpapers at ~85%, but leaves PNG alone).
/// All decoding/encoding runs on a dedicated BelowNormal thread and is serialised (FR-APPLY-5).
/// </summary>
public sealed partial class WallpaperRenderer : IWallpaperRenderer, IDisposable
{
    private readonly RenderCache _cache;
    private readonly ILogger<WallpaperRenderer> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WallpaperRenderer(RenderCache cache, ILogger<WallpaperRenderer> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> RenderAsync(WallpaperAsset asset, MonitorInfo monitor, CancellationToken cancellationToken = default)
    {
        var choice = VariantSelector.Select(monitor.PixelWidth, monitor.PixelHeight, asset.Variants)
            ?? throw new InvalidOperationException($"Wallpaper '{asset.Id}' has no usable variant on disk.");

        var variant = asset.Variants[choice.Key];
        var path = _cache.PathFor(asset.Id, choice.Key, monitor.PixelWidth, monitor.PixelHeight, asset.ContentHash, asset.Focal.X, asset.Focal.Y);
        if (RenderCache.TryUse(path))
        {
            return path;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (RenderCache.TryUse(path))
            {
                return path;
            }

            if (choice.QualityNotice is not null)
            {
                LogQualityNotice(asset.Id, choice.QualityNotice);
            }

            var temp = _cache.NewTemporaryPath();
            try
            {
                await RunOnLowPriorityThread(() => RenderToFile(variant.Path, monitor.PixelWidth, monitor.PixelHeight, asset.Focal, temp));
                _cache.Commit(temp, path);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }

            return path;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>Synchronous core, exposed for tests and the asset pipeline's review tooling.</summary>
    public static void RenderToFile(string sourcePath, int targetWidth, int targetHeight, FocalPoint focal, string destinationPath)
    {
        using var bitmap = SKBitmap.Decode(sourcePath) ?? throw new InvalidDataException($"Could not decode image '{Path.GetFileName(sourcePath)}'.");
        using var image = SKImage.FromBitmap(bitmap);

        var crop = FocalCropper.Compute(bitmap.Width, bitmap.Height, (double)targetWidth / targetHeight, focal);
        var scale = targetWidth / crop.Width;

        // Skia has no Lanczos filter. Mitchell cubic is used when scaling up or near 1:1; trilinear (mip-mapped) when
        // downscaling, which avoids the aliasing a plain cubic gives on large reductions (ADR 0003).
        var sampling = scale < 0.9
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);

        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Opaque, SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info) ?? throw new InvalidOperationException("Could not allocate the render surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        using var paint = new SKPaint { IsAntialias = true };
        var source = new SKRect((float)crop.X, (float)crop.Y, (float)(crop.X + crop.Width), (float)(crop.Y + crop.Height));
        var destination = new SKRect(0, 0, targetWidth, targetHeight);
        canvas.DrawImage(image, source, destination, sampling, paint);

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(destinationPath);
        data.SaveTo(stream);
    }

    private static Task RunOnLowPriorityThread(Action work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                work();
                completion.SetResult();
            }
#pragma warning disable CA1031 // The exception is marshalled to the awaiting task, not swallowed.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "PrettyDesk.Render",
        };
        thread.Start();
        return completion.Task;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Quality notice for {WallpaperId}: {Notice}")]
    private partial void LogQualityNotice(string wallpaperId, string notice);
}
