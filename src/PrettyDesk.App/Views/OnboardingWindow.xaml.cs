using PrettyDesk.Presentation.ViewModels;
using Wpf.Ui.Controls;

namespace PrettyDesk.App.Views;

public partial class OnboardingWindow : FluentWindow
{
    public OnboardingWindow(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Completed += Close;
        Closed += (_, _) => viewModel.Dispose();
    }
}
