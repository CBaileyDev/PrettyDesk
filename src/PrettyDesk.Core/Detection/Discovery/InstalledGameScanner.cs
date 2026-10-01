using System.Globalization;
using System.Text.Json;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;

namespace PrettyDesk.Core.Detection.Discovery;

/// <summary>
/// Local, read-only discovery of installed games (FR-DET-9): Steam <c>libraryfolders.vdf</c> + <c>appmanifest_*.acf</c> and
/// Epic <c>*.item</c> manifests only. It never opens game executables or game data. A missing or unreadable launcher is
/// not an error: it simply contributes no games.
/// </summary>
public sealed class InstalledGameScanner : IInstalledGameScanner
{
    private readonly string? _steamPath;
    private readonly string? _epicManifestDirectory;

    public InstalledGameScanner(string? steamPath, string? epicManifestDirectory)
    {
        _steamPath = steamPath;
        _epicManifestDirectory = epicManifestDirectory;
    }

    public IReadOnlyList<InstalledGame> Scan()
    {
        var games = new List<InstalledGame>();
        ScanSteam(games);
        ScanEpic(games);
        return games;
    }

    /// <summary>Joins scan results to catalog games by Steam AppID or launcher-declared exe name.</summary>
    public static IReadOnlyList<GameEntry> MatchCatalog(CatalogDocument catalog, IEnumerable<InstalledGame> installed)
    {
        var list = installed.ToList();
        var steamIds = list.Where(i => i.SteamAppId is not null).Select(i => i.SteamAppId!.Value).ToHashSet();
        var exes = list.Where(i => !string.IsNullOrEmpty(i.ExeHint)).Select(i => Path.GetFileName(i.ExeHint!.Replace('\\', '/'))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return catalog.Games
            .Where(g => g.Detection.SteamAppIds.Any(steamIds.Contains) || g.Detection.ExeNames.Any(exes.Contains))
            .ToList();
    }

    private void ScanSteam(List<InstalledGame> games)
    {
        if (string.IsNullOrEmpty(_steamPath))
        {
            return;
        }

        try
        {
            var libraries = new List<string> { _steamPath };
            var foldersFile = Path.Combine(_steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(foldersFile))
            {
                var folders = VdfParser.Parse(File.ReadAllText(foldersFile))["libraryfolders"];
                if (folders is not null)
                {
                    libraries.AddRange(folders.Children.Values.Select(f => f.Text("path")).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!));
                }
            }

            foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var apps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(apps))
                {
                    continue;
                }

                foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    var game = ParseSteamManifest(manifest, apps);
                    if (game is not null && !games.Any(g => g.SteamAppId == game.SteamAppId))
                    {
                        games.Add(game);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable Steam folder contributes nothing.
        }
    }

    private static InstalledGame? ParseSteamManifest(string manifestPath, string steamApps)
    {
        try
        {
            var state = VdfParser.Parse(File.ReadAllText(manifestPath))["AppState"];
            if (state is null || !uint.TryParse(state.Text("appid"), NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
            {
                return null;
            }

            // StateFlags bit 4 = fully installed. Half-downloaded games must not count as installed.
            if (!int.TryParse(state.Text("StateFlags"), NumberStyles.None, CultureInfo.InvariantCulture, out var flags) || (flags & 4) == 0)
            {
                return null;
            }

            var name = state.Text("name") ?? "Steam app " + appId.ToString(CultureInfo.InvariantCulture);
            var installDir = state.Text("installdir") ?? string.Empty;
            return new InstalledGame("steam", Path.Combine(steamApps, "common", installDir), null, appId, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ScanEpic(List<InstalledGame> games)
    {
        if (string.IsNullOrEmpty(_epicManifestDirectory) || !Directory.Exists(_epicManifestDirectory))
        {
            return;
        }

        try
        {
            foreach (var item in Directory.EnumerateFiles(_epicManifestDirectory, "*.item"))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(item));
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True)
                    {
                        continue;
                    }

                    var name = Text(root, "DisplayName");
                    var location = Text(root, "InstallLocation");
                    if (name is null || location is null)
                    {
                        continue;
                    }

                    games.Add(new InstalledGame("epic", location, Text(root, "LaunchExecutable"), null, name));
                }
                catch (JsonException)
                {
                    // one corrupt manifest must not hide the others
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // unreadable Epic folder contributes nothing
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
