using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrettyDesk.Core.Catalog;

/// <summary>Why a catalog was not used (for the debug log; never user-facing noise, NFR-7).</summary>
public enum CatalogRejection
{
    None,
    Malformed,
    UnsupportedSchema,
    RequiresNewerApp,
    Invalid,
    BadSignature,
    OlderThanCurrent,
}

public sealed record CatalogCheck(CatalogDocument? Document, CatalogRejection Rejection, string? Detail)
{
    public bool Ok => Rejection == CatalogRejection.None;
}

public static partial class CatalogValidator
{
    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    public static CatalogCheck ParseAndValidate(ReadOnlySpan<byte> json, Version appVersion)
    {
        CatalogDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.CatalogDocument);
        }
        catch (JsonException ex)
        {
            return new CatalogCheck(null, CatalogRejection.Malformed, ex.Message);
        }

        if (doc is null)
        {
            return new CatalogCheck(null, CatalogRejection.Malformed, "empty document");
        }

        if (doc.SchemaVersion != CatalogDocument.CurrentSchemaVersion)
        {
            return new CatalogCheck(null, CatalogRejection.UnsupportedSchema, $"schemaVersion {doc.SchemaVersion}");
        }

        if (!Version.TryParse(doc.MinAppVersion, out var min))
        {
            return new CatalogCheck(null, CatalogRejection.Invalid, "minAppVersion is not a version");
        }

        if (min > appVersion)
        {
            return new CatalogCheck(null, CatalogRejection.RequiresNewerApp, $"needs app {min}");
        }

        var problem = FindProblem(doc);
        return problem is null ? new CatalogCheck(doc, CatalogRejection.None, null) : new CatalogCheck(null, CatalogRejection.Invalid, problem);
    }

    /// <summary>Returns the first structural problem, or null when the catalog is sound.</summary>
    public static string? FindProblem(CatalogDocument doc)
    {
        if (doc.Packs is null || doc.Games is null || doc.Collections is null || doc.ExcludeExeNames is null ||
            doc.ExcludeExeNames.Any(string.IsNullOrWhiteSpace))
        {
            return "catalog lists must not contain null or empty entries";
        }

        if (!string.IsNullOrEmpty(doc.ContentBaseUrl) &&
            (!Uri.TryCreate(doc.ContentBaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps || !doc.ContentBaseUrl.EndsWith('/')))
        {
            return "contentBaseUrl must be an absolute https URL ending in '/'";
        }

        var packIds = new HashSet<string>(StringComparer.Ordinal);
        var wallpaperIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pack in doc.Packs)
        {
            if (pack is null || pack.Wallpapers is null)
            {
                return "pack and wallpaper lists must not be null";
            }

            if (string.IsNullOrWhiteSpace(pack.Id) || !packIds.Add(pack.Id))
            {
                return $"duplicate or empty pack id '{pack.Id}'";
            }

            foreach (var wallpaper in pack.Wallpapers)
            {
                if (wallpaper is null || wallpaper.Focal is null || wallpaper.Variants is null ||
                    wallpaper.Tags is null || wallpaper.SetupMatch is null ||
                    wallpaper.Tags.Any(string.IsNullOrWhiteSpace) || wallpaper.SetupMatch.Any(string.IsNullOrWhiteSpace))
                {
                    return "wallpaper data must not contain null or empty entries";
                }

                if (string.IsNullOrWhiteSpace(wallpaper.Id) || !wallpaperIds.Add(wallpaper.Id))
                {
                    return $"duplicate or empty wallpaper id '{wallpaper.Id}'";
                }

                if (wallpaper.Focal.X is < 0 or > 1 || wallpaper.Focal.Y is < 0 or > 1)
                {
                    return $"wallpaper '{wallpaper.Id}' focal point out of range";
                }

                foreach (var (key, file) in wallpaper.Variants)
                {
                    if (!Imaging.Variants.IsKnown(key))
                    {
                        return $"wallpaper '{wallpaper.Id}' has unknown variant '{key}'";
                    }

                    var fileProblem = FileProblem(file);
                    if (fileProblem is not null)
                    {
                        return $"wallpaper '{wallpaper.Id}' variant '{key}': {fileProblem}";
                    }
                }

                if (wallpaper.Thumb is not null && FileProblem(wallpaper.Thumb) is { } thumbProblem)
                {
                    return $"wallpaper '{wallpaper.Id}' thumb: {thumbProblem}";
                }
            }
        }

        var gameIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var game in doc.Games)
        {
            if (game is null || game.Detection is null || game.Detection.ExeNames is null ||
                game.Detection.SteamAppIds is null || game.Detection.PathContains is null ||
                game.Detection.WindowTitleContains is null || game.Detection.ExcludeExeNames is null ||
                game.Detection.ExeNames.Any(string.IsNullOrWhiteSpace) ||
                game.Detection.PathContains.Any(string.IsNullOrWhiteSpace) ||
                game.Detection.WindowTitleContains.Any(string.IsNullOrWhiteSpace) ||
                game.Detection.ExcludeExeNames.Any(string.IsNullOrWhiteSpace))
            {
                return "game detection rules must not contain null or empty entries";
            }

            if (string.IsNullOrWhiteSpace(game.Id) || !gameIds.Add(game.Id))
            {
                return $"duplicate or empty game id '{game.Id}'";
            }

            if (!packIds.Contains(game.PackId))
            {
                return $"game '{game.Id}' references missing pack '{game.PackId}'";
            }

            if (game.Detection.ExeNames.Count == 0 && game.Detection.SteamAppIds.Count == 0)
            {
                return $"game '{game.Id}' has no positive detection rule";
            }
        }

        foreach (var collection in doc.Collections)
        {
            if (collection is null || collection.SetupMatch is null || collection.SetupMatch.Any(string.IsNullOrWhiteSpace))
            {
                return "collection data must not contain null or empty entries";
            }

            if (!packIds.Contains(collection.PackId))
            {
                return $"collection '{collection.Id}' references missing pack '{collection.PackId}'";
            }
        }

        return null;
    }

    /// <summary>Paths must be relative to <c>contentBaseUrl</c>: no scheme, no traversal, no rooted paths (SPEC §6.2).</summary>
    public static string? FileProblem(FileRef file)
    {
        if (file is null || file.Sha256 is null)
        {
            return "file reference must not be null";
        }

        if (string.IsNullOrWhiteSpace(file.Path) || file.Path.Contains("..", StringComparison.Ordinal) || file.Path.StartsWith('/') ||
            file.Path.StartsWith('\\') || file.Path.Contains(':', StringComparison.Ordinal) || file.Path.Contains('\\', StringComparison.Ordinal))
        {
            return $"unsafe path '{file.Path}'";
        }

        if (file.Sha256.Length != 0 && !Sha256Pattern().IsMatch(file.Sha256))
        {
            return "sha256 must be 64 lowercase hex characters";
        }

        return null;
    }
}

/// <summary>Compares dotted catalog versions like <c>2026.10.01.1</c> numerically.</summary>
public static class CatalogVersion
{
    public static int Compare(string? a, string? b)
    {
        var left = Parse(a);
        var right = Parse(b);
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var l = i < left.Length ? left[i] : 0;
            var r = i < right.Length ? right[i] : 0;
            if (l != r)
            {
                return l.CompareTo(r);
            }
        }

        return 0;
    }

    private static long[] Parse(string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? []
            : version.Split('.').Select(p => long.TryParse(p, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0).ToArray();
}
