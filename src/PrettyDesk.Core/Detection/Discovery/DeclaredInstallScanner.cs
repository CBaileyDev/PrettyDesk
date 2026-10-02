using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;

namespace PrettyDesk.Core.Detection.Discovery;

public sealed record DeclaredInstallation(string DisplayName, string InstallPath);

/// <summary>Checks only declared installation roots for catalog executables, with bounded traversal.</summary>
public static class DeclaredInstallScanner
{
    public static IReadOnlyList<InstalledGame> Scan(IEnumerable<DeclaredInstallation> installations, CatalogDocument catalog)
    {
        var found = new List<InstalledGame>();
        foreach (var install in installations.Take(2048))
        {
            var game = catalog.Games.FirstOrDefault(g => string.Equals(Normalize(g.DisplayName), Normalize(install.DisplayName), StringComparison.OrdinalIgnoreCase));
            if (game is null || !Path.IsPathFullyQualified(install.InstallPath) || !Directory.Exists(install.InstallPath))
            {
                continue;
            }

            var pending = new Queue<(string Directory, int Depth)>();
            pending.Enqueue((install.InstallPath, 0));
            var examined = 0;
            while (pending.Count > 0 && examined++ < 128)
            {
                var (directory, depth) = pending.Dequeue();
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    var executable = game.Detection.ExeNames.Where(e => Path.GetFileName(e) == e)
                        .Select(e => Path.Combine(directory, e)).FirstOrDefault(File.Exists);
                    if (executable is not null)
                    {
                        var hint = Path.GetRelativePath(install.InstallPath, executable);
                        var candidate = new InstalledGame("declared", install.InstallPath, hint, null, game.DisplayName);
                        if (InstalledGameScanner.MatchCatalog(catalog, [candidate]).Any(g => g.Id == game.Id))
                        {
                            found.Add(candidate);
                        }

                        break;
                    }

                    if (depth < 4)
                    {
                        foreach (var child in Directory.EnumerateDirectories(directory).Take(128 - pending.Count))
                        {
                            pending.Enqueue((child, depth + 1));
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // An inaccessible subdirectory does not invalidate another declared installation.
                }
            }
        }

        return found.DistinctBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Normalize(string title) => title.Replace("™", "", StringComparison.Ordinal).Replace("®", "", StringComparison.Ordinal).Trim();
}
