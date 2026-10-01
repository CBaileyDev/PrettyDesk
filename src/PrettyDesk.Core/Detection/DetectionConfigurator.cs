using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Settings;

namespace PrettyDesk.Core.Detection;

/// <summary>
/// Keeps <see cref="DetectionService"/> in step with settings and the catalog: rebuilds the game registry/matcher and the
/// timings whenever either changes (FR-DET-1/7, custom games FR-CUSTOM-2).
/// </summary>
public sealed class DetectionConfigurator : IDisposable
{
    private readonly ISettingsProvider _settings;
    private readonly ICatalogProvider _catalog;
    private readonly IProcessDetailsSource? _details;
    private readonly DetectionService _service;

    public DetectionConfigurator(DetectionService service, ISettingsProvider settings, ICatalogProvider catalog, IProcessDetailsSource? details)
    {
        _service = service;
        _settings = settings;
        _catalog = catalog;
        _details = details;
        _settings.Changed += Apply;
        _catalog.Changed += Apply;
    }

    public static DetectionConfiguration Build(AppSettings settings, Catalog.CatalogDocument catalog, IProcessDetailsSource? details)
    {
        var d = settings.Detection;
        var poll = TimeSpan.FromSeconds(Math.Clamp(d.PollSeconds, 1, 10));
        var options = new DetectionOptions(
            TimeSpan.FromSeconds(Math.Clamp(d.DetectDelaySeconds, 1, 30)),
            TimeSpan.FromSeconds(Math.Clamp(d.ExitGraceSeconds, 0, 120)));
        return new DetectionConfiguration(d.Enabled, poll, options, new RuleMatcher(GameRegistry.Build(catalog, settings), details));
    }

    public void Apply() => _service.Reconfigure(Build(_settings.Current, _catalog.Current, _details));

    public void Dispose()
    {
        _settings.Changed -= Apply;
        _catalog.Changed -= Apply;
    }
}
