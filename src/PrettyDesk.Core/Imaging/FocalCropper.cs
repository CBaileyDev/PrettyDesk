using PrettyDesk.Core.Catalog;

namespace PrettyDesk.Core.Imaging;

public readonly record struct CropRect(double X, double Y, double Width, double Height);

/// <summary>Computes the sub-rectangle of a variant that matches a monitor's ratio, positioned by the wallpaper's focal point.</summary>
public static class FocalCropper
{
    public static CropRect Compute(int sourceWidth, int sourceHeight, double targetRatio, FocalPoint focal)
    {
        var sourceRatio = (double)sourceWidth / sourceHeight;
        double w = sourceWidth;
        double h = sourceHeight;

        if (sourceRatio > targetRatio)
        {
            w = sourceHeight * targetRatio;
        }
        else if (sourceRatio < targetRatio)
        {
            h = sourceWidth / targetRatio;
        }

        var cx = Math.Clamp(focal.X, 0, 1) * sourceWidth;
        var cy = Math.Clamp(focal.Y, 0, 1) * sourceHeight;
        var x = Math.Clamp(cx - (w / 2), 0, sourceWidth - w);
        var y = Math.Clamp(cy - (h / 2), 0, sourceHeight - h);
        return new CropRect(x, y, w, h);
    }
}
