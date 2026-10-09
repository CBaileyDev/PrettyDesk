using PrettyDesk.Core.Catalog;
using SkiaSharp;

namespace PrettyDesk.Core.Content;

/// <summary>
/// Sorts a user's own image into dark, mid or light by its perceived brightness, so "only dark wallpapers" can apply to it.
/// Brightness is the mean Rec. 709 luma of a sparse grid of pixels; the thresholds are chosen so that near-black wallpapers
/// (catalog "dark" tone) classify as dark and white-ground images as light.
/// </summary>
public static class ToneClassifier
{
    /// <summary>Mean luma (0-255) below which an image is dark.</summary>
    public const double DarkBelow = 100;

    /// <summary>Mean luma (0-255) at or above which an image is light.</summary>
    public const double LightFrom = 160;

    private const int GridColumns = 64;
    private const int GridRows = 36;

    public static string FromMeanLuma(double meanLuma) =>
        meanLuma < DarkBelow ? Tones.Dark : meanLuma >= LightFrom ? Tones.Light : Tones.Mid;

    /// <summary>Decodes the image and classifies it. A file that cannot be decoded is treated as mid-tone (never silently dark).</summary>
    public static string Measure(string path)
    {
        using var bitmap = TryDecode(path);
        return bitmap is null ? Tones.Mid : FromMeanLuma(MeanLuma(bitmap));
    }

    /// <summary>Mean luma over a sparse grid of the bitmap. Exposed for tests.</summary>
    public static double MeanLuma(SKBitmap bitmap)
    {
        var stepX = Math.Max(1, bitmap.Width / GridColumns);
        var stepY = Math.Max(1, bitmap.Height / GridRows);
        double sum = 0;
        var count = 0;
        for (var y = stepY / 2; y < bitmap.Height; y += stepY)
        {
            for (var x = stepX / 2; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                sum += 0.2126 * color.Red + 0.7152 * color.Green + 0.0722 * color.Blue;
                count++;
            }
        }

        return count == 0 ? 0 : sum / count;
    }

    private static SKBitmap? TryDecode(string path)
    {
        try
        {
            return SKBitmap.Decode(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
