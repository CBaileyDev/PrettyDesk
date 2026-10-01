namespace PrettyDesk.Core.Rotation;

/// <summary>
/// Shuffle bag (FR-WP-4): every wallpaper is shown once per cycle, and the first draw of a new cycle is never the
/// wallpaper that ended the previous one. State is plain data so it persists across restarts.
/// </summary>
public static class ShuffleBag
{
    public static string? Draw(ShuffleBagState state, IReadOnlyList<string> pool, Random random, IReadOnlySet<string>? avoid = null)
    {
        if (pool.Count == 0)
        {
            state.Remaining.Clear();
            state.Shown.Clear();
            return null;
        }

        var poolSet = new HashSet<string>(pool, StringComparer.Ordinal);
        state.Remaining.RemoveAll(id => !poolSet.Contains(id));
        state.Shown.RemoveAll(id => !poolSet.Contains(id));

        // Wallpapers added since the last draw join the current cycle instead of being treated as already seen.
        foreach (var id in pool)
        {
            if (!state.Remaining.Contains(id) && !state.Shown.Contains(id))
            {
                state.Remaining.Add(id);
            }
        }

        var refilled = false;
        if (state.Remaining.Count == 0)
        {
            state.Remaining.AddRange(pool.Distinct(StringComparer.Ordinal));
            state.Shown.Clear();
            refilled = true;
        }

        List<string> candidates = state.Remaining;
        if (refilled && state.Last is not null && candidates.Count > 1)
        {
            candidates = candidates.Where(id => id != state.Last).ToList();
        }

        if (avoid is { Count: > 0 })
        {
            var preferred = candidates.Where(id => !avoid.Contains(id)).ToList();
            if (preferred.Count > 0)
            {
                candidates = preferred;
            }
        }

        var pick = candidates[random.Next(candidates.Count)];
        state.Remaining.Remove(pick);
        state.Shown.Add(pick);
        state.Last = pick;
        return pick;
    }
}
