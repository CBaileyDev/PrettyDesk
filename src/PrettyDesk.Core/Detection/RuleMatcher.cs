using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;

namespace PrettyDesk.Core.Detection;

/// <summary>The processes (and/or Steam app) currently satisfying one game's rules.</summary>
public sealed record GameMatch(string GameId, IReadOnlyList<int> Pids, bool ViaSteam);

/// <summary>
/// Pure rule evaluation (FR-DET-1). A game matches when at least one positive rule (<c>exeNames</c>, <c>steamAppIds</c>)
/// matches and every secondary rule present on the game (<c>pathContains</c>, <c>windowTitleContains</c>) is satisfied
/// by the matched process. Excluded exe names never match.
/// </summary>
public sealed class RuleMatcher
{
    private readonly GameRegistry _registry;
    private readonly IProcessDetailsSource? _details;
    private readonly Dictionary<string, List<GameDefinition>> _byExe = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _liveDetailExes = new(StringComparer.OrdinalIgnoreCase);

    public bool RequiresLiveDetails(IReadOnlyList<ProcessInfo> processes) => processes.Any(p => _liveDetailExes.Contains(p.ExeName));

    public RuleMatcher(GameRegistry registry, IProcessDetailsSource? details)
    {
        _registry = registry;
        _details = details;
        foreach (var game in registry.Games)
        {
            foreach (var exe in game.Rules.ExeNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!_byExe.TryGetValue(exe, out var candidates))
                {
                    _byExe[exe] = candidates = [];
                }

                candidates.Add(game);
                if (game.Rules.PathContains.Count > 0 || game.Rules.WindowTitleContains.Count > 0)
                {
                    _liveDetailExes.Add(exe);
                }
            }
        }
    }

    public IReadOnlyList<GameMatch> Match(IReadOnlyList<ProcessInfo> processes, uint steamRunningAppId)
    {
        var matches = new List<GameMatch>();
        var matchedPids = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var process in processes)
        {
            if (!_byExe.TryGetValue(process.ExeName, out var candidates))
            {
                continue;
            }

            foreach (var game in candidates)
            {
                if (!MatchesProcess(game.Rules, process))
                {
                    continue;
                }

                if (!matchedPids.TryGetValue(game.Id, out var pids))
                {
                    matchedPids[game.Id] = pids = [];
                }

                pids.Add(process.Pid);
            }
        }

        foreach (var game in _registry.Games)
        {
            matchedPids.TryGetValue(game.Id, out var pids);

            var viaSteam = steamRunningAppId != 0 && game.Rules.SteamAppIds.Contains(steamRunningAppId);
            if (pids is not null || viaSteam)
            {
                matches.Add(new GameMatch(game.Id, pids ?? (IReadOnlyList<int>)Array.Empty<int>(), viaSteam && pids is null));
            }
        }

        return matches;
    }

    private bool MatchesProcess(DetectionRules rules, ProcessInfo process)
    {
        if (!ContainsExeName(rules.ExeNames, process.ExeName))
        {
            return false;
        }

        if (ContainsExeName(rules.ExcludeExeNames, process.ExeName))
        {
            return false;
        }

        // The global launcher list never overrides an explicit rule that pairs the exe with a window-title check
        // (this is how the ambiguous javaw.exe host stays usable for Minecraft Java).
        if (_registry.GlobalExcludeExeNames.Contains(process.ExeName) && rules.WindowTitleContains.Count == 0)
        {
            return false;
        }

        if (rules.PathContains.Count > 0 && _details is not null)
        {
            var path = _details.TryGetImagePath(process.Pid);

            // Null means access was denied (protected process): fall back to the exe-name-only match (FR-DET-3).
            if (path is not null && !rules.PathContains.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        if (rules.WindowTitleContains.Count > 0)
        {
            var title = _details?.TryGetMainWindowTitle(process.Pid);
            if (string.IsNullOrEmpty(title) || !rules.WindowTitleContains.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsExeName(IReadOnlyList<string> names, string exeName)
    {
        for (var index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], exeName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
