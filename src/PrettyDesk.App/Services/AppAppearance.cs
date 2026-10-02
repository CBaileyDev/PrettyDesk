using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Settings;
using PrettyDesk.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PrettyDesk.App.Services;

/// <summary>Native glass with readable surfaces and opaque accessibility fallbacks.</summary>
internal static class AppAppearance
{
    private static bool Updating;
    private static ISettingsProvider? Settings;
    private static readonly DesktopEffectsPreferences Effects = new();
    private static bool GlassEnabled;
    private static bool WatchingEffects;
    private static (ApplicationTheme Theme, bool HighContrast, bool Glass)? LastPalette;
    private static int RefreshPending;
    private static readonly HashSet<Views.MainWindow> PendingMaterials = [];
    public static void Initialize()
    {
        WatchEffects();
        ApplicationThemeManager.Changed += (theme, _) => ApplyPalette(theme);
        ApplicationThemeManager.ApplySystemTheme();
        ApplyPalette(ApplicationThemeManager.GetAppTheme());
    }

    private static void WatchEffects()
    {
        if (!WatchingEffects)
        {
            Effects.Changed += Refresh;
            WatchingEffects = true;
        }
    }

    internal static void ApplyMainWindowMaterial(Views.MainWindow window)
    {
        if (!window.IsLoaded) { return; }
        // Replacing WindowChrome from Loaded can re-enter WPF's first render.
        // Coalesce and defer until that render has finished.
        if (!PendingMaterials.Add(window)) { return; }
        window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (window.IsLoaded && window.IsVisible) { ApplyMaterialCore(window); }
            }
            finally { PendingMaterials.Remove(window); }
        }, DispatcherPriority.Background);
    }

    private static void ApplyMaterialCore(Views.MainWindow window)
    {
        // Genuine desktop acrylic supplies the blur; tint alone is not glass.
        window.NativeAcrylicEnabled = false;
        window.WindowBackdropType = GlassEnabled ? WindowBackdropType.Acrylic : WindowBackdropType.None;
        if (GlassEnabled && WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Acrylic) && WindowBackdrop.RemoveBackground(window))
        {
            window.NativeAcrylicEnabled = true;
            return;
        }

        window.WindowBackdropType = WindowBackdropType.None;
        window.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "DeskCanvasBrush");
    }

    public static void Bind(ISettingsProvider settings)
    {
        Settings = settings;
        settings.Changed += Refresh;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Refresh();
        SystemParameters.StaticPropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    internal static void Shutdown()
    {
        Effects.Changed -= Refresh;
        Effects.Dispose();
    }

    private static void Refresh()
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted) { return; }
        if (Interlocked.Exchange(ref RefreshPending, 1) != 0) { return; }
        app.Dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref RefreshPending, 0);
            var preference = Settings?.Current.General.Theme ?? AppThemePreference.System;
            if (preference == AppThemePreference.System)
            {
                ApplicationThemeManager.ApplySystemTheme();
            }
            else
            {
                ApplicationThemeManager.Apply(preference == AppThemePreference.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
            }

            ApplyPalette(ApplicationThemeManager.GetAppTheme());
        });
    }

    internal static void ApplyPalette(ApplicationTheme theme)
    {
        if (Updating)
        {
            return;
        }

        Updating = true;
        try
        {
            var glass = !SystemParameters.HighContrast && Effects.TransparencyEnabled && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
            var palette = (theme, SystemParameters.HighContrast, glass);
            if (LastPalette == palette) { return; }
            if (!SystemParameters.HighContrast)
            {
                ApplicationAccentColorManager.Apply(Color.FromRgb(0, 122, 255), theme);
            }
            var dark = theme == ApplicationTheme.Dark;
            var resources = Application.Current.Resources;
            GlassEnabled = glass;
            void Brush(string key, string light, string night)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? night : light));
                brush.Freeze();
                resources[key] = brush;
            }
            Brush("DeskCanvasBrush", "#EEF1F7", "#151923");
            // Keep text-bearing surfaces denser than the ambient acrylic layer.
            Brush("DeskGlassBrush", GlassEnabled ? "#D9FFFFFF" : "#FFFFFF", GlassEnabled ? "#D9242C38" : "#202530");
            Brush("DeskPanelBrush", GlassEnabled ? "#98FFFFFF" : "#F5F7FA", GlassEnabled ? "#901A202C" : "#1B202B");
            Brush("DeskInkBrush", "#182035", "#F2F6FF");
            Brush("DeskMutedBrush", "#4D5A6E", "#C2CCDC");
            Brush("DeskSoftBrush", "#DFECF2FA", "#E0374050");
            Brush("DeskStrokeBrush", "#D5DEE9", "#4A5467");
            Brush("DeskBlueBrush", "#007AFF", "#64ABFF");
            resources["DeskAmbientBrush"] = resources["DeskCanvasBrush"];
            resources["DeskShellBrush"] = resources["DeskGlassBrush"];
            resources["DeskRimBrush"] = resources["DeskStrokeBrush"];
            if (!SystemParameters.HighContrast)
            {
                LinearGradientBrush Gradient(string lightA, string lightB, string lightC, string darkA, string darkB, string darkC)
                {
                    var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                    brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(dark ? darkA : lightA), 0));
                    brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(dark ? darkB : lightB), 0.55));
                    brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(dark ? darkC : lightC), 1));
                    brush.Freeze();
                    return brush;
                }
                resources["DeskAmbientBrush"] = GlassEnabled
                    ? Gradient("#20F3F6FA", "#14F5F7FA", "#20E8EEF5", "#281A2330", "#181B2430", "#28212A36")
                    : Gradient("#DDE7F5", "#F3EAF0", "#D7E9EB", "#202B3F", "#282335", "#20343A");
                resources["DeskShellBrush"] = GlassEnabled
                    ? Gradient("#B8FFFFFF", "#90F6F8FC", "#A0EAF0F7", "#A8343F50", "#90242E3C", "#9C202B39")
                    : Gradient("#F7FBFF", "#F0F2FA", "#EAF4F5", "#2C374A", "#252639", "#273A42");
                if (!GlassEnabled)
                {
                    resources["DeskPanelBrush"] = Gradient("#F4F8FD", "#FBF7FA", "#EFF7F8", "#1B2433", "#241E2D", "#1D2B30");
                }
                resources["DeskRimBrush"] = Gradient("#C4FFFFFF", "#7096A6BA", "#507E91AB", "#788D9DB5", "#385D6D85", "#484A5A72");
            }
            resources["DeskArtVisibility"] = SystemParameters.HighContrast ? Visibility.Collapsed : Visibility.Visible;
            Brush("ToggleSwitchFillOnPointerOver", "#006CE4", "#7BB8FF");
            Brush("ToggleSwitchFillOnPressed", "#005CC4", "#519AEF");
            if (SystemParameters.HighContrast)
            {
                foreach (var key in new[] { "DeskCanvasBrush", "DeskGlassBrush", "DeskPanelBrush", "DeskSoftBrush", "DeskAmbientBrush", "DeskShellBrush" })
                {
                    resources[key] = SystemColors.WindowBrush;
                }

                resources["DeskInkBrush"] = SystemColors.WindowTextBrush;
                resources["DeskMutedBrush"] = SystemColors.WindowTextBrush;
                resources["DeskBlueBrush"] = SystemColors.HotTrackBrush;
                resources["DeskStrokeBrush"] = SystemColors.WindowTextBrush;
                resources["DeskRimBrush"] = SystemColors.WindowTextBrush;
            }
            foreach (var window in Application.Current.Windows.OfType<Views.MainWindow>()) { ApplyMainWindowMaterial(window); }
            LastPalette = palette;
        }
        finally
        {
            Updating = false;
        }
    }
}
