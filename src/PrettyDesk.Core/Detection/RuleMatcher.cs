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

    public RuleMatcher(GameRegistry registry, IProcessDetailsSource? details)
    {
        _registry = registry;
        _details = details;
    }

    public IReadOnlyList<GameMatch> Match(IReadOnlyList<ProcessInfo> processes, uint steamRunningAppId)
    {
        var matches = new List<GameMatch>();
        foreach (var game in _registry.Games)
        {
            var pids = new List<int>();
            foreach (var process in processes)
            {
                if (MatchesProcess(game.Rules, process))
                {
                    pids.Add(process.Pid);
                }
            }

            var viaSteam = steamRunningAppId != 0 && game.Rules.SteamAppIds.Contains(steamRunningAppId);
            if (pids.Count > 0 || viaSteam)
            {
                matches.Add(new GameMatch(game.Id, pids, viaSteam && pids.Count == 0));
            }
        }

        return matches;
    }

    private bool MatchesProcess(DetectionRules rules, ProcessInfo process)
    {
        if (!rules.ExeNames.Contains(process.ExeName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (rules.ExcludeExeNames.Contains(process.ExeName, StringComparer.OrdinalIgnoreCase))
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
}
