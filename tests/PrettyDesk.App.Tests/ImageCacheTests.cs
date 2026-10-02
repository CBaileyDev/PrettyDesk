using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrettyDesk.App.Converters;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;

namespace PrettyDesk.App.Tests;

public sealed class ImageCacheTests
{
    [Fact]
    public void Repeated_thumbnails_reuse_decoded_pixels_but_replaced_files_refresh()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "thumbnail.png");
        WriteImage(path, 255);
        var converter = new PathToImageConverter();
        object? Load(string width) => converter.Convert(path, typeof(ImageSource), width, CultureInfo.InvariantCulture);
        var first = Load("16").ShouldBeOfType<BitmapImage>();
        first.IsFrozen.ShouldBeTrue();
        Load("16").ShouldBeSameAs(first);
        Load("8").ShouldNotBeSameAs(first);
        WriteImage(path, 0); // Also proves the converter released the file handle.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));
        var replacement = Load("16").ShouldBeOfType<BitmapImage>();
        replacement.ShouldNotBeSameAs(first);
        var pixels = new byte[16 * 16 * 4];
        replacement.CopyPixels(pixels, 16 * 4, 0);
        pixels[0].ShouldBe((byte)0);
        converter.ClearCache();
        Load("16").ShouldNotBeSameAs(replacement);
        File.Delete(path);
        Load("16").ShouldBeNull();
    }

    private static void WriteImage(string path, byte value)
    {
        var pixels = Enumerable.Repeat(value, 16 * 16 * 4).ToArray();
        var bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
