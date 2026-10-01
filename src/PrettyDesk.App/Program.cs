using PrettyDesk.App.Services;
using Velopack;

namespace PrettyDesk.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: Velopack's install/update/uninstall hooks start this exe with special arguments, run the callback, and exit.
        // Updates that were downloaded earlier are applied here too, before any window or single-instance mutex exists.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => InstallHooks.BeforeUninstall())
            .Run();

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            // Another PrettyDesk is running; it has been asked to show its window.
            return 0;
        }

        var app = new App(instance, args);
        app.InitializeComponent();
        return app.Run();
    }
}
