using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrettyDesk.Presentation.ViewModels;

namespace PrettyDesk.App.Converters;

/// <summary>true → Collapsed, false → Visible.</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>null / empty string → Collapsed, anything else → Visible.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible when the bound enum equals the converter parameter's name (e.g. <c>ConverterParameter=Style</c>).</summary>
public sealed class EnumEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>True when the bound enum equals the converter parameter's name; ConvertBack returns the enum for a checked RadioButton.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}

/// <summary>
/// File path → image decoded at display size (<c>DecodePixelWidth</c>, SPEC §7), loaded fully so the file is not locked.
/// Missing or corrupt files give null so the designed placeholder shows instead of an error.
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    private const long CacheBudget = 16 * 1024 * 1024;
    private readonly Dictionary<(string Path, int Width, long Stamp, long Length), BitmapImage> _cache = [];
    private readonly Queue<(string Path, int Width, long Stamp, long Length)> _order = [];
    private long _cacheBytes;

    public int DecodeWidth { get; set; } = 480;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var decodeWidth = Math.Clamp(parameter is string text && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var width) ? width : DecodeWidth, 1, 2048);
            var file = new FileInfo(path);
            var key = (file.FullName, decodeWidth, file.LastWriteTimeUtc.Ticks, file.Length);
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var cached)) { return cached; }
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = decodeWidth;
            // A stream avoids WPF's URI cache serving stale pixels after replacement.
            using var stream = File.OpenRead(path);
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            var bytes = (long)image.PixelWidth * image.PixelHeight * 4;
            lock (_cache)
            {
                if (bytes <= CacheBudget && !_cache.ContainsKey(key))
                {
                    while (_cacheBytes + bytes > CacheBudget && _order.TryDequeue(out var oldest))
                    {
                        if (_cache.Remove(oldest, out var removed)) { _cacheBytes -= (long)removed.PixelWidth * removed.PixelHeight * 4; }
                    }
                    _cache.Add(key, image);
                    _order.Enqueue(key);
                    _cacheBytes += bytes;
                }
            }
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UriFormatException or FileFormatException)
        {
            return null;
        }
    }

    internal void ClearCache()
    {
        lock (_cache)
        {
            _cache.Clear();
            _order.Clear();
            _cacheBytes = 0;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Banner severity → accent brush.</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        BannerSeverity.Error => Application.Current.TryFindResource("SystemFillColorCriticalBrush") ?? Brushes.IndianRed,
        BannerSeverity.Warning => Application.Current.TryFindResource("SystemFillColorCautionBrush") ?? Brushes.Orange,
        _ => Application.Current.TryFindResource("AccentFillColorDefaultBrush") ?? Brushes.SteelBlue,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Double → GridLength-free helper used to size monitor rectangles inside the preview canvas.</summary>
public sealed class DoubleToRoundedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? Math.Round(d, 1) : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
