namespace PrettyDesk.Core.Orchestration;

/// <summary>Optional desktop surfaces stop drawing when they cannot be useful.</summary>
public static class DesktopSurfacePolicy
{
    public static bool ShouldShow(bool enabled, bool gameActive, bool sessionLocked, bool batterySaver) =>
        enabled && !gameActive && !sessionLocked && !batterySaver;
}
