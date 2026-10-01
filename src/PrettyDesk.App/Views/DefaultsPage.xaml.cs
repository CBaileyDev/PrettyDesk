using System.Windows;
using System.Windows.Controls;
using PrettyDesk.Presentation.ViewModels;

namespace PrettyDesk.App.Views;

public partial class DefaultsPage : UserControl
{
    public DefaultsPage() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is DefaultsViewModel vm && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await vm.ImportAsync(paths);
        }
    }
}
