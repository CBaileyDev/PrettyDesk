using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace PrettyDesk.Core.Settings;

public enum RotationIntervalKind
{
    /// <summary>Rotate after a fixed wall-clock duration (FR-WP-3).</summary>
    Duration,

    /// <summary>Rotate on every unlock/login.</summary>
    Unlock,

    /// <summary>Never rotate mid-session; advance when the next session starts (game default, FR-WP-3).</summary>
    Session,

    /// <summary>Never rotate automatically.</summary>
    Never,
}

/// <summary>Serialises as an ISO-8601 duration (<c>PT30M</c>) or one of <c>unlock</c>, <c>session</c>, <c>never</c>.</summary>
[JsonConverter(typeof(RotationIntervalJsonConverter))]
public readonly record struct RotationInterval
{
    public static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(7);

    private RotationInterval(RotationIntervalKind kind, TimeSpan duration)
    {
        Kind = kind;
        Duration = duration;
    }

    public RotationIntervalKind Kind { get; }

    public TimeSpan Duration { get; }

    public static RotationInterval Unlock => new(RotationIntervalKind.Unlock, TimeSpan.Zero);

    public static RotationInterval Session => new(RotationIntervalKind.Session, TimeSpan.Zero);

    public static RotationInterval Never => new(RotationIntervalKind.Never, TimeSpan.Zero);

    public static RotationInterval Every(TimeSpan duration)
    {
        if (duration < MinDuration || duration > MaxDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Interval must be between 1 minute and 7 days (FR-WP-3).");
        }

        return new RotationInterval(RotationIntervalKind.Duration, duration);
    }

    public static RotationInterval Parse(string text)
    {
        if (!TryParse(text, out var value))
        {
            throw new FormatException($"'{text}' is not a valid rotation interval.");
        }

        return value;
    }

    public static bool TryParse(string? text, out RotationInterval value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "unlock":
                value = Unlock;
                return true;
            case "session":
                value = Session;
                return true;
            case "never":
                value = Never;
                return true;
        }

        try
        {
            var span = XmlConvert.ToTimeSpan(text.Trim().ToUpperInvariant());
            // Out-of-range durations from a hand-edited file are clamped rather than rejected.
            span = span < MinDuration ? MinDuration : span > MaxDuration ? MaxDuration : span;
            value = new RotationInterval(RotationIntervalKind.Duration, span);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public override string ToString() => Kind switch
    {
        RotationIntervalKind.Unlock => "unlock",
        RotationIntervalKind.Session => "session",
        RotationIntervalKind.Never => "never",
        _ => XmlConvert.ToString(Duration),
    };

    /// <summary>The standard picker choices from FR-WP-3.</summary>
    public static IReadOnlyList<RotationInterval> Presets { get; } =
    [
        Every(TimeSpan.FromMinutes(5)),
        Every(TimeSpan.FromMinutes(15)),
        Every(TimeSpan.FromMinutes(30)),
        Every(TimeSpan.FromHours(1)),
        Every(TimeSpan.FromHours(3)),
        Every(TimeSpan.FromHours(6)),
        Every(TimeSpan.FromDays(1)),
        Unlock,
    ];

    public string ToHumanString(CultureInfo? culture = null)
    {
        _ = culture;
        return Kind switch
        {
            RotationIntervalKind.Unlock => "On every unlock",
            RotationIntervalKind.Session => "Next session",
            RotationIntervalKind.Never => "Never",
            _ when Duration.TotalDays >= 1 && Duration.TotalDays % 1 == 0 => Duration.TotalDays == 1 ? "Daily" : $"Every {Duration.TotalDays:0} days",
            _ when Duration.TotalHours >= 1 && Duration.TotalHours % 1 == 0 => Duration.TotalHours == 1 ? "Every hour" : $"Every {Duration.TotalHours:0} hours",
            _ => $"Every {Duration.TotalMinutes:0} minutes",
        };
    }
}

public sealed class RotationIntervalJsonConverter : JsonConverter<RotationInterval>
{
    public override RotationInterval Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        // A bad value in a hand-edited file falls back to the documented default instead of failing the whole load.
        return RotationInterval.TryParse(text, out var value) ? value : RotationInterval.Every(TimeSpan.FromMinutes(30));
    }

    public override void Write(Utf8JsonWriter writer, RotationInterval value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
