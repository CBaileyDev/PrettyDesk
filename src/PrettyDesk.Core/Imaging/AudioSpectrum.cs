namespace PrettyDesk.Core.Imaging;

/// <summary>Bounded log-frequency display magnitudes from transient mono playback samples.</summary>
public static class AudioSpectrum
{
    public const int BandCount = 12;

    public static double[] Analyze(ReadOnlySpan<float> samples, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        var levels = new double[BandCount];
        if (samples.IsEmpty)
        {
            return levels;
        }

        for (var band = 0; band < BandCount; band++)
        {
            var frequency = 80 * Math.Pow(100, (double)band / (BandCount - 1));
            if (frequency >= sampleRate / 2.0)
            {
                continue;
            }

            var coefficient = 2 * Math.Cos(2 * Math.PI * frequency / sampleRate);
            double previous = 0, beforePrevious = 0;
            for (var i = 0; i < Math.Min(samples.Length, 2048); i++)
            {
                var sample = float.IsFinite(samples[i]) ? Math.Clamp(samples[i], -1, 1) : 0;
                var value = sample + (coefficient * previous) - beforePrevious;
                beforePrevious = previous;
                previous = value;
            }

            var magnitude = Math.Sqrt(Math.Max(0, (previous * previous) + (beforePrevious * beforePrevious) - (coefficient * previous * beforePrevious))) / Math.Min(samples.Length, 2048);
            levels[band] = Math.Clamp(Math.Sqrt(magnitude * 4), 0, 1);
        }

        return levels;
    }
}
