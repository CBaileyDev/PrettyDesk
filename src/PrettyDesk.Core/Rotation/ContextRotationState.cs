namespace PrettyDesk.Core.Rotation;

/// <summary>Persisted shuffle-bag state for one context (FR-WP-4).</summary>
public sealed class ShuffleBagState
{
    /// <summary>Wallpapers not yet shown in the current cycle.</summary>
    public List<string> Remaining { get; set; } = [];

    /// <summary>Wallpapers already shown in the current cycle (so a new addition is not mistaken for "already seen").</summary>
    public List<string> Shown { get; set; } = [];

    /// <summary>The last wallpaper drawn; never drawn first after a refill.</summary>
    public string? Last { get; set; }
}

/// <summary>Persisted position of one rotation context: what is showing, since when, and the bag (FR-WP-4/5/6).</summary>
public sealed class ContextRotationState
{
    public string? CurrentWallpaperId { get; set; }

    /// <summary>Wall-clock time the current wallpaper was chosen. Deadlines are derived from this (FR-WP-6).</summary>
    public DateTimeOffset? ShownAt { get; set; }

    public ShuffleBagState Bag { get; set; } = new();

    /// <summary>Set by an unlock event; consumed by the next resolve of a context using the unlock interval.</summary>
    public bool UnlockPending { get; set; }
}
