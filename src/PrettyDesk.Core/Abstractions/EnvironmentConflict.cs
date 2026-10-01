namespace PrettyDesk.Core.Abstractions;

/// <summary>Environment situations that affect wallpaper changes. They are detected and explained, never silently "fixed" (FR-APPLY-7).</summary>
[Flags]
public enum EnvironmentConflict
{
    None = 0,

    /// <summary>"Your organization manages your wallpaper": applying is disabled.</summary>
    PolicyLocked = 1,

    /// <summary>Wallpaper Engine draws over the desktop, so PrettyDesk changes will not be visible.</summary>
    WallpaperEngine = 2,

    /// <summary>Lively Wallpaper draws over the desktop, so PrettyDesk changes will not be visible.</summary>
    Lively = 4,

    /// <summary>Windows Spotlight or the built-in slideshow is the background; applying switches it to a picture.</summary>
    SpotlightOrSlideshow = 8,
}

public interface IEnvironmentConflictSource
{
    EnvironmentConflict Detect();
}
