using System.Windows;

namespace PrettyDesk.App;

public partial class App : Application
{
    private readonly SingleInstance _instance;
    private readonly string[] _args;

    public App(SingleInstance instance, string[] args)
    {
        _instance = instance;
        _args = args;
    }
}
