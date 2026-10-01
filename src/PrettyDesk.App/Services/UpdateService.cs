using PrettyDesk.Presentation.Services;

namespace PrettyDesk.App.Services;

/// <summary>
/// Placeholder until Velopack is wired in M6: reports honestly that this build cannot update itself, so About shows the
/// "download the newest installer" message instead of a button that does nothing.
/// </summary>
public sealed class NotSupportedUpdateService : IUpdateService
{
    public UpdateState State { get; } = new(UpdateStateKind.NotSupported);

#pragma warning disable CS0067 // never raised: the state never changes for this implementation
    public event Action? Changed;
#pragma warning restore CS0067

    public Task CheckAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void ApplyAndRestart()
    {
    }
}
