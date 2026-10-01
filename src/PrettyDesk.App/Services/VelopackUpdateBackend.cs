using PrettyDesk.Core.Abstractions;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Presentation.ViewModels;
using Velopack;
using Velopack.Sources;

namespace PrettyDesk.App.Services;

/// <summary>
/// Velopack against the GitHub Releases feed of the app repository (SPEC §9): channels win-{arch}-stable and win-{arch}-beta. Only installed copies
/// update themselves; portable zips and dev builds report <see cref="IsSupported"/> = false and the About page says so.
/// Signature-verified by Velopack (package checksums); nothing about the user or machine is sent beyond the HTTPS request itself.
/// </summary>
public sealed class VelopackUpdateBackend : IUpdateBackend
{
    private readonly ISettingsProvider _settings;
    private readonly Lazy<UpdateManager?> _manager;

    /// <summary>The opaque token handed to <see cref="UpdateCoordinator"/> for a release found by this session.</summary>
    private sealed record Found(UpdateManager Manager, UpdateInfo Info);

    public VelopackUpdateBackend(ISettingsProvider settings)
    {
        _settings = settings;
        _manager = new Lazy<UpdateManager?>(TryCreateDefault);
    }

    public bool IsSupported => _manager.Value is { IsInstalled: true, IsPortable: false };

    public AvailableUpdate? PendingRestart =>
        IsSupported && _manager.Value!.UpdatePendingRestart is { } pending ? new AvailableUpdate(pending.Version.ToString(), pending) : null;

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        // The channel can change while the app runs, so each check builds a manager for the channel chosen now.
        var manager = Create(_settings.Current.General.BetaUpdates);
        var info = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
        return info is null ? null : new AvailableUpdate(info.TargetFullRelease.Version.ToString(), new Found(manager, info));
    }

    public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellationToken)
    {
        var found = (Found)update.Token;
        return found.Manager.DownloadUpdatesAsync(found.Info, progress, cancellationToken);
    }

    public void ApplyAndRestart(AvailableUpdate update)
    {
        switch (update.Token)
        {
            case Found found:
                found.Manager.ApplyUpdatesAndRestart(found.Info.TargetFullRelease, [AppConstants.BackgroundArgument]);
                break;
            case VelopackAsset pending:
                _manager.Value!.ApplyUpdatesAndRestart(pending, [AppConstants.BackgroundArgument]);
                break;
        }
    }

    /// <summary>A dev build or an unusual environment can make Velopack's locator fail; that simply means "cannot self-update".</summary>
    private static UpdateManager? TryCreateDefault()
    {
        try
        {
            return Create(beta: false);
        }
#pragma warning disable CA1031 // Any locator failure means this copy is not an installed Velopack app.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static UpdateManager Create(bool beta) =>
        new(
            new GithubSource(AppLinks.Repository, accessToken: null, prerelease: beta),
            new UpdateOptions { ExplicitChannel = UpdateChannels.ForThisMachine(beta) });
}
