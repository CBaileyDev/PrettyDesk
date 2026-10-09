using System.Windows;
using System.Windows.Media;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Settings;
using Wpf.Ui.Appearance;

namespace PrettyDesk.App.Services;

/// <summary>
/// Applies the light, dark or Windows high-contrast palette (<c>Resources/Palette.*.xaml</c>) and the accent colour. The
/// app is drawn with opaque surfaces, so there is no backdrop or transparency preference to track.
/// </summary>
internal static class AppAppearance
{
    private static readonly Color Accent = Color.FromRgb(0x5B, 0x5B, 0xD6);

    private static bool Updating;
    private static int RefreshPending;
    private static ISettingsProvider? Settings;
    private static ResourceDictionary? Palette;
    private static (ApplicationTheme Theme, bool HighContrast)? Applied;

    public static void Initialize()
    {
        ApplicationThemeManager.Changed += (theme, _) => ApplyPalette(theme);
        ApplicationThemeManager.ApplySystemTheme();
        ApplyPalette(ApplicationThemeManager.GetAppTheme());
    }

    public static void Bind(ISettingsProvider settings)
    {
        Settings = settings;
        settings.Changed += Refresh;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Refresh();
        SystemParameters.StaticPropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Re-reads the theme setting on the UI thread; bursts of OS notifications collapse into one pass.</summary>
    private static void Refresh()
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted) { return; }
        if (Interlocked.Exchange(ref RefreshPending, 1) != 0) { return; }
        app.Dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref RefreshPending, 0);
            switch (Settings?.Current.General.Theme ?? AppThemePreference.System)
            {
                case AppThemePreference.Dark:
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    break;
                case AppThemePreference.Light:
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    break;
                default:
                    ApplicationThemeManager.ApplySystemTheme();
                    break;
            }

            ApplyPalette(ApplicationThemeManager.GetAppTheme());
        });
    }

    internal static void ApplyPalette(ApplicationTheme theme)
    {
        if (Updating) { return; }

        var highContrast = SystemParameters.HighContrast;
        if (Applied == (theme, highContrast)) { return; }

        Updating = true;
        try
        {
            if (!highContrast)
            {
                ApplicationAccentColorManager.Apply(Accent, theme);
            }

            var name = highContrast ? "HighContrast" : theme == ApplicationTheme.Dark ? "Dark" : "Light";
            var merged = Application.Current.Resources.MergedDictionaries;
            if (Palette is not null) { merged.Remove(Palette); }

            // Added last so it wins over the WPF-UI defaults it overrides (for example the toggle switch fills).
            Palette = new ResourceDictionary { Source = new Uri($"pack://application:,,,/PrettyDesk;component/Resources/Palette.{name}.xaml") };
            merged.Add(Palette);
            Applied = (theme, highContrast);
        }
        finally
        {
            Updating = false;
        }
    }
}
