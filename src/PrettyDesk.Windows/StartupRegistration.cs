using Microsoft.Win32;

namespace PrettyDesk.Windows;

/// <summary>
/// "Start with Windows" through the per-user <c>HKCU\...\Run</c> key only (FR-APP-3, NFR-6): no HKLM, no scheduled task,
/// no service. The entry launches the app with <c>--background</c> so it starts in the tray without a window.
/// </summary>
public sealed class StartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string BackgroundArgument = "--background";

    private readonly string _valueName;

    public StartupRegistration(string valueName = "PrettyDesk") => _valueName = valueName;

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(_valueName) is string { Length: > 0 };
        }
    }

    public void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(_valueName, CommandLineFor(executablePath), RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    /// <summary>The exact value written to the Run key.</summary>
    public static string CommandLineFor(string executablePath) => $"\"{executablePath}\" {BackgroundArgument}";

    /// <summary>True when the stored command points at a different exe (e.g. after a portable copy moved), so it can be refreshed.</summary>
    public bool IsStale(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(_valueName) is string current && !string.Equals(current, CommandLineFor(executablePath), StringComparison.OrdinalIgnoreCase);
    }
}
