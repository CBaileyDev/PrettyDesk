using System.Globalization;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Presentation.Resources;

namespace PrettyDesk.Presentation.Formatting;

public static class ByteSize
{
    public static string Format(long bytes)
    {
        var culture = Strings.Culture ?? CultureInfo.CurrentUICulture;
        return bytes switch
        {
            < 1024 => Strings.Format(Strings.Bytes_B, bytes.ToString(culture)),
            < 1024L * 1024 => Strings.Format(Strings.Bytes_KB, (bytes / 1024.0).ToString("0.#", culture)),
            < 1024L * 1024 * 1024 => Strings.Format(Strings.Bytes_MB, (bytes / (1024.0 * 1024)).ToString("0.#", culture)),
            _ => Strings.Format(Strings.Bytes_GB, (bytes / (1024.0 * 1024 * 1024)).ToString("0.##", culture)),
        };
    }
}

public static class RelativeTime
{
    /// <summary>"in 12 min", "in 1 h 5 min"… for a future span (negative spans read as "in under a minute").</summary>
    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return Strings.Time_UnderMinute;
        }

        var minutes = (int)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 60)
        {
            return Strings.Format(Strings.Time_Minutes, minutes);
        }

        if (minutes < 24 * 60)
        {
            var hours = minutes / 60;
            var rest = minutes % 60;
            return rest == 0 ? Strings.Format(Strings.Time_Hours, hours) : Strings.Format(Strings.Time_HoursMinutes, hours, rest);
        }

        var days = (int)Math.Round(span.TotalDays, MidpointRounding.AwayFromZero);
        return days <= 1 ? Strings.Time_OneDay : Strings.Format(Strings.Time_Days, days);
    }
}

public sealed record StatusText(string Headline, string? Detail);

/// <summary>Turns the orchestrator's structured status into the words shown on Home and in the tray (SPEC FR-APP-2, §7).</summary>
public static class StatusFormatter
{
    public static StatusText Describe(OrchestratorStatus? status, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        if (status is null)
        {
            return new StatusText(Strings.Status_Starting, null);
        }

        var culture = Strings.Culture ?? CultureInfo.CurrentUICulture;
        var timeZone = zone ?? TimeZoneInfo.Local;
        string? failure = status.ApplyFailed ? Strings.Status_ApplyFailed : null;

        switch (status.Mode)
        {
            case OrchestratorMode.Game:
                return new StatusText(
                    Strings.Format(Strings.Status_Playing, status.GameName),
                    failure ?? (status.PoolSize > 1 ? Strings.Format(Strings.Status_PlayingDetailPosition, status.PoolIndex + 1, status.PoolSize) : null));

            case OrchestratorMode.GameGrace:
                return new StatusText(Strings.Format(Strings.Status_Playing, status.GameName), failure ?? Strings.Status_GameEnded);

            case OrchestratorMode.GameLoading:
                return new StatusText(Strings.Format(Strings.Status_GameLoading, status.GameName), Strings.Status_GameLoadingDetail);

            case OrchestratorMode.Paused:
                return status.PausedUntil is { } until
                    ? new StatusText(Strings.Format(Strings.Status_PausedUntil, TimeZoneInfo.ConvertTime(until, timeZone).ToString("t", culture)), null)
                    : new StatusText(Strings.Status_Paused, Strings.Status_PausedIndefinite);

            case OrchestratorMode.Blocked:
                return new StatusText(Strings.Status_Blocked, Strings.Status_BlockedDetail);

            case OrchestratorMode.Preview:
                return new StatusText(Strings.Status_Preview, failure ?? Strings.Status_PreviewDetail);

            default:
                if (status.NoContent)
                {
                    return new StatusText(Strings.Status_DefaultNoContent, Strings.Status_DefaultNoContentDetail);
                }

                var title = status.DefaultTitle ?? string.Empty;
                var headline = status.IsRotating ? Strings.Format(Strings.Status_DefaultRotating, title) : Strings.Format(Strings.Status_DefaultFixed, title);
                var detail = failure ?? (status.IsRotating && status.NextChange is { } next
                    ? Strings.Format(Strings.Status_DefaultNextChange, RelativeTime.Format(next - now))
                    : null);
                return new StatusText(headline, detail);
        }
    }
}
