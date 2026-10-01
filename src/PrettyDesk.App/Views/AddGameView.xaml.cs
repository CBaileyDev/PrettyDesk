using System.Windows;
using System.Windows.Controls;
using PrettyDesk.Presentation.ViewModels;

namespace PrettyDesk.App.Views;

public partial class AddGameView : UserControl
{
    public AddGameView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AddGameViewModel vm && vm.RefreshCommand.CanExecute(null))
        {
            vm.RefreshCommand.Execute(null);
        }
    }
}
