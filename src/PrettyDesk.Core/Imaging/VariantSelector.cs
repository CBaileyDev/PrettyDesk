using PrettyDesk.Core.Catalog;

namespace PrettyDesk.Core.Imaging;

public sealed record VariantChoice(string Key, double Ratio, bool IsFallback, string? QualityNotice);

/// <summary>
/// Chooses the variant whose aspect ratio is closest to the monitor's: smallest <c>|ln(monitorRatio) − ln(variantRatio)|</c>
/// among the variants that exist (SPEC §6.1, FR-APPLY-2).
/// </summary>
public static class VariantSelector
{
    public static VariantChoice? Select(int monitorWidth, int monitorHeight, IReadOnlyDictionary<string, LocalVariant> available) =>
        Select(monitorWidth, monitorHeight, available.ToDictionary(kv => kv.Key, kv => RatioOf(kv.Value)));

    public static VariantChoice? Select(int monitorWidth, int monitorHeight, IReadOnlyDictionary<string, double> availableRatios)
    {
        if (availableRatios.Count == 0 || monitorWidth <= 0 || monitorHeight <= 0)
        {
            return null;
        }

        var monitorRatio = (double)monitorWidth / monitorHeight;
        var ideal = Variants.All.OrderBy(v => Distance(monitorRatio, v.Ratio)).First();

        // Portrait monitors without a 9x16 variant crop the 16x9 one (SPEC §6.1) rather than a closer-ratio landscape.
        if (monitorRatio < 1 && !availableRatios.ContainsKey(Variants.Portrait9x16) && availableRatios.ContainsKey(Variants.Landscape16x9))
        {
            return new VariantChoice(Variants.Landscape16x9, availableRatios[Variants.Landscape16x9], true, "Portrait monitor: no 9x16 variant, cropping 16x9");
        }

        if (availableRatios.TryGetValue(ideal.Key, out var idealRatio))
        {
            return new VariantChoice(ideal.Key, idealRatio, false, null);
        }

        var best = availableRatios.OrderBy(kv => Distance(monitorRatio, kv.Value)).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
        return new VariantChoice(best.Key, best.Value, true, $"Variant {ideal.Key} missing, using {best.Key}");
    }

    private static double RatioOf(LocalVariant variant) =>
        Variants.Find(variant.Key)?.Ratio ?? (variant.Height == 0 ? 1.0 : (double)variant.Width / variant.Height);

    private static double Distance(double a, double b) => Math.Abs(Math.Log(a) - Math.Log(b));
}
