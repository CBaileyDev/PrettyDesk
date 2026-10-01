using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace PrettyDesk.App.Views;

/// <summary>A small modal with one or more buttons. <see cref="Choice"/> is the index pressed, or -1 when dismissed.</summary>
public partial class MessageDialog : FluentWindow
{
    public MessageDialog(string title, string message, IReadOnlyList<string> buttons)
    {
        InitializeComponent();
        Title = title;
        Bar.Title = title;
        MessageText.Text = message;
        AutomationProperties.SetName(this, title);

        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Wpf.Ui.Controls.Button
            {
                Content = buttons[i],
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                MinWidth = 88,
                IsDefault = i == 0,
                IsCancel = i == buttons.Count - 1 && buttons.Count > 1,
            };
            if (i == 0)
            {
                button.Appearance = ControlAppearance.Primary;
            }

            AutomationProperties.SetName(button, buttons[i]);
            button.Click += (_, _) =>
            {
                Choice = index;
                Close();
            };
            ButtonRow.Children.Add(button);
        }

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Choice = buttons.Count > 1 ? buttons.Count - 1 : 0;
                Close();
            }
        };
    }

    public int Choice { get; private set; } = -1;
}
