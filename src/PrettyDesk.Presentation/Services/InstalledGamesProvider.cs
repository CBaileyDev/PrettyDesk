using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Detection.Discovery;

namespace PrettyDesk.Presentation.Services;

/// <summary>Scans local launcher manifests off the UI thread and maps them to catalog game ids (FR-DET-9).</summary>
public sealed class InstalledGamesProvider : IInstalledGamesProvider
{
    private readonly IInstalledGameScanner _scanner;
    private readonly ICatalogProvider _catalog;

    public InstalledGamesProvider(IInstalledGameScanner scanner, ICatalogProvider catalog)
    {
        _scanner = scanner;
        _catalog = catalog;
    }

    public Task<IReadOnlySet<string>> GetInstalledGameIdsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlySet<string>>(
            () => InstalledGameScanner.MatchCatalog(_catalog.Current, _scanner.Scan()).Select(g => g.Id).ToHashSet(StringComparer.Ordinal),
            cancellationToken);
}
