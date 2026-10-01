using System.Windows;
using System.Windows.Controls;
using PrettyDesk.Presentation.ViewModels;

namespace PrettyDesk.App.Views;

public partial class LibraryPage : UserControl
{
    public LibraryPage() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Which games are installed is read from launcher manifests off the UI thread (FR-DET-9).
        if (DataContext is LibraryViewModel vm && vm.LoadInstalledCommand.CanExecute(null))
        {
            vm.LoadInstalledCommand.Execute(null);
        }
    }
}
