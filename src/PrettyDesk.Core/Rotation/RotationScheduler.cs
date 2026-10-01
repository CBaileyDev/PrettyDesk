using PrettyDesk.Core.Settings;

namespace PrettyDesk.Core.Rotation;

/// <summary>How one context rotates. <see cref="AutoRotationPaused"/> is true while Battery Saver pauses rotation (FR-WP-9).</summary>
public sealed record RotationPolicy(RotationOrder Order, RotationInterval Interval, bool AutoRotationPaused = false);

/// <summary>The wallpapers a context shows, one per monitor slot (slot 0 first), and whether any of them changed.</summary>
public sealed record RotationResult(IReadOnlyList<string> Ids, bool Changed)
{
    public static RotationResult None { get; } = new([], false);

    public string? WallpaperId => Ids.Count > 0 ? Ids[0] : null;
}

/// <summary>
/// Owns the rotation position of every context (FR-WP-3..6). Time is evaluated against wall-clock deadlines
/// (<c>ShownAt + interval</c>), never cumulative timers, so resume-from-sleep or a long suspend rotates at most once.
/// A context that is not being resolved (e.g. the default context while a game is active) is simply left untouched,
/// which gives FR-WP-5 for free.
/// </summary>
public sealed class RotationScheduler
{
    private readonly IDictionary<string, ContextRotationState> _states;
    private readonly TimeProvider _time;
    private readonly Random _random;

    public RotationScheduler(IDictionary<string, ContextRotationState> states, TimeProvider time, Random random)
    {
        _states = states;
        _time = time;
        _random = random;
    }

    /// <summary>Raised after any mutation, so the host can persist <c>state.json</c>.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// The wallpapers this context should show now, advancing at most once if its deadline has passed.
    /// <paramref name="slots"/> is the number of monitors that need a distinct wallpaper (1 when all monitors share one).
    /// </summary>
    public RotationResult Resolve(string contextKey, IReadOnlyList<string> pool, RotationPolicy policy, int slots = 1)
    {
        if (pool.Count == 0 || slots < 1)
        {
            return RotationResult.None;
        }

        var state = GetState(contextKey);
        var now = _time.GetUtcNow();
        var existing = ExistingSlots(state, pool);

        if (existing.Count > 0 && existing[0] == state.CurrentWallpaperId)
        {
            if (state.ShownAt > now)
            {
                // The clock moved backwards; re-anchor so the deadline stays sane.
                state.ShownAt = now;
                StateChanged?.Invoke();
            }

            if (!IsDue(state, policy, now))
            {
                return existing.Count >= slots ? new RotationResult(existing.Take(slots).ToList(), false) : TopUp(state, pool, policy, existing, slots);
            }
        }

        return DrawAll(state, pool, policy, now, slots);
    }

    /// <summary>Called when a context becomes current. Session-interval contexts advance here ("next wallpaper next session").</summary>
    public RotationResult EnterContext(string contextKey, IReadOnlyList<string> pool, RotationPolicy policy, int slots = 1)
    {
        if (pool.Count == 0 || slots < 1)
        {
            return RotationResult.None;
        }

        var state = GetState(contextKey);
        if (policy.Interval.Kind == RotationIntervalKind.Session && state.CurrentWallpaperId is not null && pool.Contains(state.CurrentWallpaperId))
        {
            return DrawAll(state, pool, policy, _time.GetUtcNow(), slots);
        }

        return Resolve(contextKey, pool, policy, slots);
    }

    /// <summary>Manual "Next wallpaper" (FR-WP-7).</summary>
    public RotationResult Advance(string contextKey, IReadOnlyList<string> pool, RotationPolicy policy, int slots = 1) =>
        pool.Count == 0 || slots < 1 ? RotationResult.None : DrawAll(GetState(contextKey), pool, policy, _time.GetUtcNow(), slots);

    /// <summary>Marks every context so unlock-interval contexts rotate on their next resolve (FR-WP-3).</summary>
    public void NotifyUnlock()
    {
        foreach (var state in _states.Values)
        {
            state.UnlockPending = true;
        }

        StateChanged?.Invoke();
    }

    /// <summary>When the context's current wallpaper expires, or null when it never expires on a timer.</summary>
    public DateTimeOffset? NextDeadline(string contextKey, RotationPolicy policy)
    {
        if (policy.Interval.Kind != RotationIntervalKind.Duration || policy.AutoRotationPaused)
        {
            return null;
        }

        return _states.TryGetValue(contextKey, out var state) && state.ShownAt is { } shown && state.CurrentWallpaperId is not null
            ? shown + policy.Interval.Duration
            : null;
    }

    public string? Current(string contextKey) => _states.TryGetValue(contextKey, out var s) ? s.CurrentWallpaperId : null;

    /// <summary>Forgets a context entirely.</summary>
    public void Forget(string contextKey)
    {
        if (_states.Remove(contextKey))
        {
            StateChanged?.Invoke();
        }
    }

    private static List<string> ExistingSlots(ContextRotationState state, IReadOnlyList<string> pool)
    {
        var slots = new List<string>();
        if (state.CurrentWallpaperId is null || !pool.Contains(state.CurrentWallpaperId))
        {
            return slots;
        }

        slots.Add(state.CurrentWallpaperId);
        foreach (var extra in state.AdditionalWallpaperIds)
        {
            if (!pool.Contains(extra) || slots.Contains(extra))
            {
                // A removed (or duplicated) extra slot ends the contiguous run; the missing slots are topped up.
                break;
            }

            slots.Add(extra);
        }

        return slots;
    }

    private static bool IsDue(ContextRotationState state, RotationPolicy policy, DateTimeOffset now)
    {
        if (policy.AutoRotationPaused)
        {
            return false;
        }

        return policy.Interval.Kind switch
        {
            RotationIntervalKind.Duration => state.ShownAt is null || now >= state.ShownAt.Value + policy.Interval.Duration,
            RotationIntervalKind.Unlock => state.UnlockPending,
            _ => false,
        };
    }

    /// <summary>A monitor was plugged in (or a slot's wallpaper vanished): fill the missing slots without touching the others.</summary>
    private RotationResult TopUp(ContextRotationState state, IReadOnlyList<string> pool, RotationPolicy policy, List<string> existing, int slots)
    {
        var ids = new List<string>(existing);
        while (ids.Count < slots)
        {
            ids.Add(DrawOne(state, pool, policy, ids));
        }

        Store(state, ids, state.ShownAt ?? _time.GetUtcNow(), keepShownAt: true);
        StateChanged?.Invoke();
        return new RotationResult(ids, true);
    }

    private RotationResult DrawAll(ContextRotationState state, IReadOnlyList<string> pool, RotationPolicy policy, DateTimeOffset now, int slots)
    {
        var previous = new List<string>();
        if (state.CurrentWallpaperId is not null)
        {
            previous.Add(state.CurrentWallpaperId);
            previous.AddRange(state.AdditionalWallpaperIds);
        }

        var ids = new List<string>(slots);
        for (var slot = 0; slot < slots; slot++)
        {
            ids.Add(DrawOne(state, pool, policy, ids));
        }

        Store(state, ids, now, keepShownAt: false);
        state.UnlockPending = false;
        var changed = !ids.SequenceEqual(previous.Take(slots));
        StateChanged?.Invoke();
        return new RotationResult(ids, changed);
    }

    private string DrawOne(ContextRotationState state, IReadOnlyList<string> pool, RotationPolicy policy, List<string> alreadyDrawn)
    {
        var avoid = alreadyDrawn.Count == 0 ? null : alreadyDrawn.ToHashSet(StringComparer.Ordinal);
        if (policy.Order == RotationOrder.Sequential)
        {
            return NextSequential(pool, alreadyDrawn.Count > 0 ? alreadyDrawn[^1] : state.CurrentWallpaperId, avoid);
        }

        return ShuffleBag.Draw(state.Bag, pool, _random, avoid)!;
    }

    private static void Store(ContextRotationState state, List<string> ids, DateTimeOffset shownAt, bool keepShownAt)
    {
        state.CurrentWallpaperId = ids[0];
        state.AdditionalWallpaperIds = ids.Skip(1).ToList();
        if (!keepShownAt)
        {
            state.ShownAt = shownAt;
        }
    }

    private static string NextSequential(IReadOnlyList<string> pool, string? after, HashSet<string>? avoid)
    {
        var start = after is null ? -1 : IndexOf(pool, after);
        string? fallback = null;
        for (var step = 1; step <= pool.Count; step++)
        {
            var candidate = pool[(start + step + pool.Count) % pool.Count];
            fallback ??= candidate;
            if (avoid is null || !avoid.Contains(candidate))
            {
                return candidate;
            }
        }

        return fallback!;
    }

    private static int IndexOf(IReadOnlyList<string> pool, string id)
    {
        for (var i = 0; i < pool.Count; i++)
        {
            if (pool[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    private ContextRotationState GetState(string key)
    {
        if (!_states.TryGetValue(key, out var state))
        {
            state = new ContextRotationState();
            _states[key] = state;
        }

        return state;
    }
}
