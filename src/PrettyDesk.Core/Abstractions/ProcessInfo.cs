namespace PrettyDesk.Core.Abstractions;

/// <summary>One row of a process snapshot. Only metadata obtainable without opening the process (FR-DET-3).</summary>
public sealed record ProcessInfo(int Pid, string ExeName, int ParentPid, DateTimeOffset? StartTime = null);

/// <summary>The current foreground window's owner process.</summary>
public sealed record ForegroundInfo(int Pid, string? ExeName);

/// <summary>A display attached to the PC, in physical pixels (FR-MON-1).</summary>
public sealed record MonitorInfo(
    string Id,
    int Left,
    int Top,
    int PixelWidth,
    int PixelHeight,
    bool IsPrimary)
{
    /// <summary>Aspect ratio as presented to the user (already reflects rotation).</summary>
    public double AspectRatio => PixelHeight == 0 ? 1.0 : (double)PixelWidth / PixelHeight;
}
