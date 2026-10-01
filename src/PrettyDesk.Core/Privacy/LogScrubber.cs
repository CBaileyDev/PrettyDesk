namespace PrettyDesk.Core.Privacy;

/// <summary>
/// Removes the user name and profile path from text before it reaches a log file (SPEC §10: "never log … file paths under the
/// user profile beyond %LOCALAPPDATA%\PrettyDesk, or the user name"). Paths inside PrettyDesk's own data folder are kept
/// because they are useful and contain nothing personal once the profile prefix is abstracted.
/// </summary>
public static class LogScrubber
{
    public static string Scrub(string text, string userName, string userProfilePath, string? localAppDataPath = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;
        foreach (var variant in Variants(localAppDataPath))
        {
            result = result.Replace(variant, "%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var variant in Variants(userProfilePath))
        {
            result = result.Replace(variant, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        // Anything that still names the user (e.g. "C:\Users\jane\..." under a different drive or a UNC path).
        if (userName.Length >= 3)
        {
            result = result.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static IEnumerable<string> Variants(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        var trimmed = path.TrimEnd('\\', '/');
        yield return trimmed;
        yield return trimmed.Replace('\\', '/');
        yield return trimmed.Replace("\\", "\\\\", StringComparison.Ordinal);
    }
}
