using CommunityToolkit.Mvvm.ComponentModel;

namespace PrettyDesk.Presentation.ViewModels;

/// <summary>
/// Base for page view models. Gives every async action a loading state and a human error message (SPEC §7 quality bar) and a
/// disposal hook so event subscriptions are released when the window closes (FR-APP-4).
/// </summary>
public abstract partial class ViewModelBase : ObservableObject, IDisposable
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool IsNotBusy => !IsBusy;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Runs an async action with a busy flag; failures become a friendly message instead of an exception.</summary>
    protected async Task RunAsync(Func<Task> action, string errorMessage)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // cancelled by the user or by shutdown: not an error
        }
#pragma warning disable CA1031 // NFR-13/SPEC §7: every failure becomes a human message with a next step, never a crash.
        catch (Exception)
#pragma warning restore CA1031
        {
            ErrorMessage = errorMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
