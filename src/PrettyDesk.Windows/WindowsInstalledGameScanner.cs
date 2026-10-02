using System.Security;
using Microsoft.Win32;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Detection.Discovery;

namespace PrettyDesk.Windows;

/// <summary>Launcher manifests plus read-only Windows uninstall declarations; never scans arbitrary drives.</summary>
public sealed class WindowsInstalledGameScanner(IInstalledGameScanner launchers, ICatalogProvider catalog) : IInstalledGameScanner
{
    public IReadOnlyList<InstalledGame> Scan() => launchers.Scan()
        .Concat(DeclaredInstallScanner.Scan(ReadDeclarations(), catalog.Current))
        .DistinctBy(g => (g.Source, g.DisplayName, g.InstallPath)).ToList();

    private static List<DeclaredInstallation> ReadDeclarations()
    {
        var result = new List<DeclaredInstallation>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: false);
                    foreach (var name in uninstall?.GetSubKeyNames().Take(2048) ?? [])
                    {
                        try
                        {
                            using var entry = uninstall!.OpenSubKey(name, writable: false);
                            if (entry?.GetValue("DisplayName") is string title && entry.GetValue("InstallLocation") is string path)
                            {
                                result.Add(new DeclaredInstallation(title, path));
                            }
                        }
                        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
                        {
                            // Read-only inventory: unavailable entries contribute nothing.
                        }
                    }
                }
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
                {
                    // A denied hive must not prevent scanning the other sources.
                }
            }
        }

        return result;
    }
}
