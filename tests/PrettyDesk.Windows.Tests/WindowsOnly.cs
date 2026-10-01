using Xunit;

namespace PrettyDesk.Windows.Tests;

internal static class WindowsOnly
{
    /// <summary>These tests drive real Win32/COM and only run on a Windows runner; elsewhere they are skipped, not faked.</summary>
    public static void Require() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows-only integration test");
}
