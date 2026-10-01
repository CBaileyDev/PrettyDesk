using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Settings;

namespace PrettyDesk.Core.Detection;

/// <summary>A game the detector can recognise: either from the catalog or added by the user (FR-CUSTOM).</summary>
public sealed record GameDefinition(
    string Id,
    string DisplayName,
    DetectionRules Rules,
    string? PackId,
    bool IsCustom);

/// <summary>
/// The merged, enabled-only set of games used for matching. Custom rules win over catalog rules for the same exe
/// (FR-CUSTOM-2): a catalog game never matches an exe that a custom game claims.
/// </summary>
public sealed class GameRegistry
{
    private GameRegistry(IReadOnlyList<GameDefinition> games, IReadOnlySet<string> globalExcludes)
    {
        Games = games;
        GlobalExcludeExeNames = globalExcludes;
    }

    public IReadOnlyList<GameDefinition> Games { get; }

    public IReadOnlySet<string> GlobalExcludeExeNames { get; }

    public GameDefinition? Find(string id) => Games.FirstOrDefault(g => g.Id == id);

    public static GameRegistry Build(CatalogDocument catalog, AppSettings settings)
    {
        var customExes = new HashSet<string>(
            settings.CustomGames.SelectMany(c => c.ExeNames),
            StringComparer.OrdinalIgnoreCase);

        var games = new List<GameDefinition>();
        foreach (var entry in catalog.Games)
        {
            if (!settings.IsGameEnabled(entry.Id))
            {
                continue;
            }

            var rules = entry.Detection with
            {
                ExeNames = entry.Detection.ExeNames.Where(e => !customExes.Contains(e)).ToList(),
            };

            // A game whose every exe was claimed by a custom game keeps only its non-exe rules.
            if (rules.ExeNames.Count == 0 && rules.SteamAppIds.Count == 0)
            {
                continue;
            }

            games.Add(new GameDefinition(entry.Id, entry.DisplayName, rules, entry.PackId, IsCustom: false));
        }

        foreach (var custom in settings.CustomGames)
        {
            if (custom.ExeNames.Count == 0 || !settings.IsGameEnabled(custom.Id))
            {
                continue;
            }

            var rules = new DetectionRules { ExeNames = custom.ExeNames.ToList() };
            games.Add(new GameDefinition(custom.Id, custom.DisplayName, rules, PackId: null, IsCustom: true));
        }

        var excludes = new HashSet<string>(catalog.ExcludeExeNames, StringComparer.OrdinalIgnoreCase);
        return new GameRegistry(games, excludes);
    }
}
