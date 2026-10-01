namespace PrettyDesk.App;

/// <summary>Where PrettyDesk keeps its data (SPEC §5.6). Siblings of the Velopack <c>current\</c> folder, so updates never touch them.</summary>
public sealed class AppPaths
{
    public AppPaths(string? rootOverride = null)
    {
        Root = rootOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.ProductName);
        LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        BundledContent = Path.Combine(AppContext.BaseDirectory, "content");
    }

    public string Root { get; }

    public string LocalAppData { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string StateFile => Path.Combine(Root, "state.json");

    public string CatalogDirectory => Path.Combine(Root, "catalog");

    public string Packs => Path.Combine(Root, "packs");

    public string RenderCache => Path.Combine(Root, "cache", "render");

    public string UserImages => Path.Combine(Root, "user");

    public string Backup => Path.Combine(Root, "backup");

    public string Logs => Path.Combine(Root, "logs");

    public string BundledContent { get; }

    public string BundledCatalog => Path.Combine(BundledContent, "catalog.json");

    public string BundledStarter => Path.Combine(BundledContent, "starter");

    public void EnsureCreated()
    {
        foreach (var directory in new[] { Root, CatalogDirectory, Packs, RenderCache, UserImages, Backup, Logs })
        {
            Directory.CreateDirectory(directory);
        }
    }
}
