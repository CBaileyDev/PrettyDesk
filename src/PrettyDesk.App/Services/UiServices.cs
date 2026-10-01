using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Windows;

namespace PrettyDesk.App.Services;

public sealed class WpfDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}

public sealed class DialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText) =>
        Task.FromResult(Show(title, message, [confirmText, cancelText]) == 0);

    public Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> buttons) =>
        Task.FromResult(Show(title, message, buttons));

    public Task ShowMessageAsync(string title, string message)
    {
        Show(title, message, [Presentation.Resources.Strings.Common_OK]);
        return Task.CompletedTask;
    }

    private static int Show(string title, string message, IReadOnlyList<string> buttons)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(() => Show(title, message, buttons));
        }

        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w is not Views.MessageDialog)
            ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible && w is not Views.MessageDialog);
        var dialog = new Views.MessageDialog(title, message, buttons) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }
}

public sealed class FilePickerService : IFilePicker
{
    public IReadOnlyList<string> PickImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = Presentation.Resources.Strings.Defaults_AddImages,
            Filter = "Images (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp",
            Multiselect = true,
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public string? PickExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = Presentation.Resources.Strings.Add_BrowseExe,
            Filter = "Programs (*.exe)|*.exe",
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

public sealed class ExternalLauncher : IExternalLauncher
{
    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void OpenUrl(string url)
    {
        // Only web links ever leave the app; anything else (file:, ms-settings:, custom schemes) is refused.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}

public sealed class StartupService : IStartupService
{
    private readonly StartupRegistration _registration = new();

    public bool IsEnabled => _registration.IsEnabled;

    public void Apply(bool enabled)
    {
        if (enabled)
        {
            _registration.Enable(Environment.ProcessPath ?? throw new InvalidOperationException("The app path is unknown."));
        }
        else
        {
            _registration.Disable();
        }
    }
}

public sealed class RunningAppsAdapter : IRunningAppsProvider
{
    private readonly ProcessSource _processes;

    public RunningAppsAdapter(ProcessSource processes) => _processes = processes;

    public IReadOnlyList<RunningApp> GetWindowedApps() =>
        WindowedApps.Enumerate(_processes).Select(a => new RunningApp(a.ExeName, a.Title)).ToList();
}
