using System.Windows.Controls;

namespace PrettyDesk.App.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    private void OnOpenColors(object sender, System.Windows.RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:colors") { UseShellExecute = true });
}
