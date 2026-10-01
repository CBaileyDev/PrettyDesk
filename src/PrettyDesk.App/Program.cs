using System.Windows;

namespace PrettyDesk.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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
