using System.Reflection;

namespace PrettyDesk.App;

public static class AppConstants
{
    public const string ProductName = "PrettyDesk";
    public const string BackgroundArgument = "--background";

    /// <summary>The build-time content host (SPEC §6.3). Empty means an offline-only build.</summary>
    public static string ContentBaseUrl { get; } =
        typeof(AppConstants).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "ContentBaseUrl")?.Value ?? string.Empty;

    public static Version Version { get; } = typeof(AppConstants).Assembly.GetName().Version ?? new Version(1, 0, 0);

    public static string DisplayVersion { get; } =
        typeof(AppConstants).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Version.ToString(3);
}
