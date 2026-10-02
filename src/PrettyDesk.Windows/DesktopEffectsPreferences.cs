using System.Runtime.InteropServices;
using global::Windows.UI.ViewManagement;

namespace PrettyDesk.Windows;

/// <summary>Read-only OS transparency preference; failure selects an opaque application surface.</summary>
public sealed class DesktopEffectsPreferences : IDisposable
{
    private readonly UISettings? _settings;
    public DesktopEffectsPreferences()
    {
        try
        {
            _settings = new UISettings();
            _settings.AdvancedEffectsEnabledChanged += OnChanged;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            _settings = null;
        }
    }

    public event Action? Changed;
    public bool TransparencyEnabled
    {
        get
        {
            try { return _settings?.AdvancedEffectsEnabled == true; }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException) { return false; }
        }
    }

    private void OnChanged(UISettings sender, object args) => Changed?.Invoke();
    public void Dispose()
    {
        if (_settings is not null) { _settings.AdvancedEffectsEnabledChanged -= OnChanged; }
    }
}
