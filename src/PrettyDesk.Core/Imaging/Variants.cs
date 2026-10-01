namespace PrettyDesk.Core.Imaging;

/// <summary>An aspect-ratio family of a wallpaper (SPEC §6.1).</summary>
public sealed record VariantSpec(string Key, int Width, int Height)
{
    public double Ratio => (double)Width / Height;
}

public static class Variants
{
    public const string Landscape16x9 = "16x9";
    public const string Landscape16x10 = "16x10";
    public const string Landscape3x2 = "3x2";
    public const string Ultrawide21x9 = "21x9";
    public const string SuperUltrawide32x9 = "32x9";
    public const string Portrait9x16 = "9x16";

    /// <summary>Key used for a user-supplied image, which has a single "variant" with its own native ratio.</summary>
    public const string User = "user";

    public static IReadOnlyList<VariantSpec> All { get; } =
    [
        new(Landscape16x9, 3840, 2160),
        new(Landscape16x10, 3840, 2400),
        new(Landscape3x2, 3000, 2000),
        new(Ultrawide21x9, 5120, 2160),
        new(SuperUltrawide32x9, 5120, 1440),
        new(Portrait9x16, 2160, 3840),
    ];

    public static VariantSpec? Find(string key) => All.FirstOrDefault(v => v.Key == key);

    public static bool IsKnown(string key) => Find(key) is not null;
}
