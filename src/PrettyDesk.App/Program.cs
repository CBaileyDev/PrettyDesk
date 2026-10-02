using PrettyDesk.App.Services;
using Velopack;

namespace PrettyDesk.App;

public static class Program
{
    internal static System.Diagnostics.Stopwatch StartupClock { get; } = System.Diagnostics.Stopwatch.StartNew();

    [STAThread]
    public static int Main(string[] args)
    {
        _ = StartupClock;
        // Must run first: Velopack's install/update/uninstall hooks start this exe with special arguments, run the callback, and exit.
        // Updates that were downloaded earlier are applied here too, before any window or single-instance mutex exists.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => InstallHooks.BeforeUninstall())
            .Run();

#if PRETTYDESK_ACCEPTANCE
        if (args.Length > 0 && args[0].StartsWith("--acceptance-", StringComparison.Ordinal))
        {
            return AcceptanceHarness.RunAsync(args).GetAwaiter().GetResult();
        }
#endif

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
