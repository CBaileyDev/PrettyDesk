using System.Windows.Controls;
using PrettyDesk.Presentation.ViewModels;
using Wpf.Ui.Controls;

namespace PrettyDesk.App.Views;

public partial class MainWindow : FluentWindow
{
    private bool _syncing;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => SyncSelection();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.SelectedKind))
            {
                SyncSelection();
            }
        };
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            NavList.SelectedItem = Shell.NavItems.FirstOrDefault(n => n.Kind == Shell.SelectedKind);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && NavList.SelectedItem is NavItem item)
        {
            Shell.NavigateToCommand.Execute(item.Kind);
        }
    }
}
