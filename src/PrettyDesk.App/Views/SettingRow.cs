using System.Windows;

namespace PrettyDesk.App.Views;

/// <summary>Help text shown under a setting's title by <c>SettingRowStyle</c>.</summary>
public static class SettingRow
{
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.RegisterAttached("Description", typeof(string), typeof(SettingRow), new PropertyMetadata(null));

    public static string? GetDescription(DependencyObject element) => (string?)element.GetValue(DescriptionProperty);

    public static void SetDescription(DependencyObject element, string? value) => element.SetValue(DescriptionProperty, value);
}
