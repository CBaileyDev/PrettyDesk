namespace PrettyDesk.Core.Detection;

public sealed record DetectionOptions(TimeSpan DetectDelay, TimeSpan ExitGrace)
{
    public static DetectionOptions Default { get; } = new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));
}

/// <summary>The game the desktop should currently reflect, and whether it has already exited (inside the grace period).</summary>
public sealed record ActiveGame(string GameId, bool InGrace);

public sealed record ActiveGameChange(ActiveGame? Previous, ActiveGame? Current);

/// <summary>
/// Pure, deterministic debounce / grace / priority logic (FR-DET-4/5/6). All time comes from the caller, so tests drive
/// it with a fake clock. The caller feeds it observations and re-observes at <see cref="NextDeadline"/>.
/// </summary>
public sealed class GameSessionTracker
{
    private readonly Dictionary<string, Tracked> _tracked = [];
    private ActiveGame? _active;
    private DetectionOptions _options;

    public GameSessionTracker(DetectionOptions options)
    {
        _options = options;
    }

    public ActiveGame? Active => _active;

    /// <summary>Earliest time at which re-observing could change the outcome (debounce expiry or grace expiry), if any.</summary>
    public DateTimeOffset? NextDeadline { get; private set; }

    public event Action<ActiveGameChange>? ActiveChanged;

    public void UpdateOptions(DetectionOptions options) => _options = options;

    /// <param name="now">Current wall-clock time.</param>
    /// <param name="matches">Games whose rules currently match; disabled games must already be filtered out.</param>
    /// <param name="foregroundPid">PID owning the foreground window, or 0 when unknown.</param>
    public void Observe(DateTimeOffset now, IReadOnlyList<GameMatch> matches, int foregroundPid)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var match in matches)
        {
            present.Add(match.GameId);
            if (!_tracked.TryGetValue(match.GameId, out var tracked))
            {
                tracked = new Tracked(match.GameId, now);
                _tracked[match.GameId] = tracked;
            }

            tracked.LastSeen = now;
            tracked.Present = true;
            if (!tracked.Qualified && now - tracked.FirstSeen >= _options.DetectDelay)
            {
                tracked.Qualified = true;
            }

            if (foregroundPid != 0 && match.Pids.Contains(foregroundPid))
            {
                tracked.LastForeground = now;
            }
        }

        foreach (var id in _tracked.Keys.ToList())
        {
            if (present.Contains(id))
            {
                continue;
            }

            var tracked = _tracked[id];
            tracked.Present = false;

            // Never qualified (crash on launch, launcher hand-off): forget immediately so the next start debounces afresh.
            // Qualified: keep through the grace period so self-restarts keep the context (FR-DET-5).
            if (!tracked.Qualified || now - tracked.LastSeen >= _options.ExitGrace)
            {
                _tracked.Remove(id);
            }
        }

        Resolve();
    }

    /// <summary>Drops all tracking, e.g. when detection is globally disabled or paused.</summary>
    public void Reset()
    {
        _tracked.Clear();
        Resolve();
    }

    private void Resolve()
    {
        var candidates = _tracked.Values.Where(t => t.Qualified).ToList();

        // Currently-running games outrank games merely inside their exit grace.
        var chosen = candidates
            .OrderByDescending(t => t.Present)
            .ThenByDescending(t => t.LastForeground ?? DateTimeOffset.MinValue)
            .ThenByDescending(t => t.FirstSeen)
            .ThenBy(t => t.GameId, StringComparer.Ordinal)
            .FirstOrDefault();

        var next = chosen is null ? null : new ActiveGame(chosen.GameId, InGrace: !chosen.Present);
        NextDeadline = ComputeDeadline();

        if (next == _active)
        {
            return;
        }

        var change = new ActiveGameChange(_active, next);
        _active = next;
        ActiveChanged?.Invoke(change);
    }

    private DateTimeOffset? ComputeDeadline()
    {
        DateTimeOffset? earliest = null;
        foreach (var tracked in _tracked.Values)
        {
            DateTimeOffset? deadline = tracked.Qualified
                ? (tracked.Present ? null : tracked.LastSeen + _options.ExitGrace)
                : tracked.FirstSeen + _options.DetectDelay;

            if (deadline is not null && (earliest is null || deadline < earliest))
            {
                earliest = deadline;
            }
        }

        return earliest;
    }

    private sealed class Tracked(string gameId, DateTimeOffset firstSeen)
    {
        public string GameId { get; } = gameId;
        public DateTimeOffset FirstSeen { get; } = firstSeen;
        public DateTimeOffset LastSeen { get; set; } = firstSeen;
        public DateTimeOffset? LastForeground { get; set; }
        public bool Present { get; set; } = true;
        public bool Qualified { get; set; }
    }
}
