using System.Globalization;
using System.Text;
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
    private const int MaxManifestBytes = 1024 * 1024;
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
        return catalog.Games
            .Where(g => g.Detection.SteamAppIds.Any(steamIds.Contains) || list.Any(i =>
                MatchesExeHint(g.Detection, i) || i.Source == "epic" &&
                !string.IsNullOrWhiteSpace(i.ExeHint) &&
                string.Equals(NormalizeTitle(g.DisplayName), NormalizeTitle(i.DisplayName), StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    // Epic often declares a bootstrapper rather than the gameplay exe. Its primary, complete manifest still identifies
    // the installed title. Match the whole title, allowing only trademark marks and surrounding whitespace to differ.
    private static string NormalizeTitle(string title) => title.Normalize(NormalizationForm.FormC)
        .Replace("®", "", StringComparison.Ordinal).Replace("™", "", StringComparison.Ordinal).Trim();

    private static bool MatchesExeHint(DetectionRules rules, InstalledGame installed)
    {
        if (string.IsNullOrEmpty(installed.ExeHint))
        {
            return false;
        }

        var exe = Path.GetFileName(installed.ExeHint.Replace('\\', '/'));
        var path = installed.InstallPath + "/" + installed.ExeHint;
        return rules.ExeNames.Contains(exe, StringComparer.OrdinalIgnoreCase) &&
            !rules.ExcludeExeNames.Contains(exe, StringComparer.OrdinalIgnoreCase) &&
            (rules.PathContains.Count == 0 || rules.PathContains.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)));
    }

    private void ScanSteam(List<InstalledGame> games)
    {
        if (string.IsNullOrEmpty(_steamPath))
        {
            return;
        }

        var libraries = new List<string> { _steamPath };
        try
        {
            var foldersFile = Path.Combine(_steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(foldersFile) && new FileInfo(foldersFile).Length <= MaxManifestBytes)
            {
                var folders = VdfParser.Parse(File.ReadAllText(foldersFile))["libraryfolders"];
                if (folders is not null)
                {
                    libraries.AddRange(folders.Children.Values.Select(f => f.Text("path") ?? f.Value).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!));
                }
            }

        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Still scan the primary library when Steam is rewriting or locking libraryfolders.vdf.
        }

        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A broken or unavailable library must not hide games in later libraries.
            }
        }
    }

    private static InstalledGame? ParseSteamManifest(string manifestPath, string steamApps)
    {
        try
        {
            if (new FileInfo(manifestPath).Length > MaxManifestBytes)
            {
                return null;
            }

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
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
                    if (new FileInfo(item).Length > MaxManifestBytes)
                    {
                        continue;
                    }

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
                    var executable = Text(root, "LaunchExecutable");
                    if (name is null || location is null || executable is null ||
                        !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(location))
                    {
                        continue;
                    }

                    games.Add(new InstalledGame("epic", location, executable, null, name));
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
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
