using SkiaSharp;

namespace PrettyDesk.Core.Tests.Support;

internal static class ImageFactory
{
    /// <summary>Writes a gradient image with an optional bright marker rectangle (fractions of width/height).</summary>
    public static void Write(string path, int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png, (double X, double Y, double W, double H)? marker = null)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(30, 40, 60));
        using var paint = new SKPaint { Color = new SKColor(30, 40, 60) };
        if (marker is { } m)
        {
            paint.Color = new SKColor(255, 0, 0);
            canvas.DrawRect((float)(m.X * width), (float)(m.Y * height), (float)(m.W * width), (float)(m.H * height), paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    public static SKBitmap Decode(string path) => SKBitmap.Decode(path);
}
