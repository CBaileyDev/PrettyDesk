using Microsoft.Win32;
using PrettyDesk.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.System.Power;

namespace PrettyDesk.Windows;

/// <summary>
/// Windows facts that influence applying: light/dark theme (FR-WP-8), Battery Saver (FR-WP-9) and wallpaper policy locks
/// (FR-APPLY-7). Registry access is strictly read-only; nothing here ever writes to HKLM.
/// </summary>
public sealed class WindowsSystemState : ISystemState, IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemPolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string ActiveDesktopPolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop";
    private const string WallpapersKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";

    private readonly ITimer _poll;
    private bool _light;
    private bool _batterySaver;
    private bool _locked;

    public WindowsSystemState(TimeProvider time)
    {
        _light = ReadLightTheme();
        _batterySaver = ReadBatterySaver();
        _locked = ReadPolicyLocked();
        SystemEvents.UserPreferenceChanged += OnSystemEvent;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Battery Saver has no cheap change notification through SystemEvents; one tiny call a minute is invisible.
        _poll = time.CreateTimer(_ => Refresh(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public bool IsLightTheme => _light;

    public bool IsBatterySaverOn => _batterySaver;

    public bool IsWallpaperPolicyLocked => _locked;

    public event Action? Changed;

    /// <summary>The background type the user had before PrettyDesk. Advisory: values come from community documentation.</summary>
    public static string ReadBackgroundType()
    {
        // 0 picture, 1 solid colour, 2 slideshow, 3 Windows Spotlight. These values are not in Microsoft's official docs
        // (community-sourced), so anything unknown is treated as a plain picture.
        using var key = Registry.CurrentUser.OpenSubKey(WallpapersKey);
        return key?.GetValue("BackgroundType") is int value
            ? value switch { 1 => "solid", 2 => "slideshow", 3 => "spotlight", _ => "picture" }
            : "picture";
    }

    public void Refresh()
    {
        var light = ReadLightTheme();
        var saver = ReadBatterySaver();
        var locked = ReadPolicyLocked();
        if (light == _light && saver == _batterySaver && locked == _locked)
        {
            return;
        }

        _light = light;
        _batterySaver = saver;
        _locked = locked;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemEvent;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _poll.Dispose();
    }

    private void OnSystemEvent(object? sender, UserPreferenceChangedEventArgs e) => Refresh();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) => Refresh();

    private static bool ReadLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    private static bool ReadBatterySaver()
    {
        return PInvoke.GetSystemPowerStatus(out var status) && status.SystemStatusFlag == 1;
    }

    private static bool ReadPolicyLocked()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var system = hive.OpenSubKey(SystemPolicyKey);
            if (system?.GetValue("Wallpaper") is string { Length: > 0 })
            {
                return true;
            }

            using var activeDesktop = hive.OpenSubKey(ActiveDesktopPolicyKey);
            if (activeDesktop?.GetValue("NoChangingWallPaper") is int and not 0)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Detects situations that change what the user will see (FR-APPLY-7). It only reports; nothing is "fixed" silently.</summary>
public sealed class EnvironmentConflictDetector : IEnvironmentConflictSource
{
    private readonly IProcessSource _processes;
    private readonly ISystemState _system;

    public EnvironmentConflictDetector(IProcessSource processes, ISystemState system)
    {
        _processes = processes;
        _system = system;
    }

    public EnvironmentConflict Detect()
    {
        var result = EnvironmentConflict.None;
        if (_system.IsWallpaperPolicyLocked)
        {
            result |= EnvironmentConflict.PolicyLocked;
        }

        foreach (var process in _processes.Snapshot())
        {
            if (process.ExeName.Equals("wallpaper32.exe", StringComparison.OrdinalIgnoreCase) || process.ExeName.Equals("wallpaper64.exe", StringComparison.OrdinalIgnoreCase))
            {
                result |= EnvironmentConflict.WallpaperEngine;
            }
            else if (process.ExeName.Equals("Lively.exe", StringComparison.OrdinalIgnoreCase))
            {
                result |= EnvironmentConflict.Lively;
            }
        }

        if (WindowsSystemState.ReadBackgroundType() is "slideshow" or "spotlight")
        {
            result |= EnvironmentConflict.SpotlightOrSlideshow;
        }

        return result;
    }
}
