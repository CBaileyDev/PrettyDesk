using PrettyDesk.Windows;
using Shouldly;
using Xunit;

namespace PrettyDesk.Windows.Tests;

public class ProcessTests
{
    [Fact]
    public void Toolhelp_snapshot_contains_the_current_process()
    {
        WindowsOnly.Require();

        var processes = new ProcessSource().Snapshot();

        var self = processes.SingleOrDefault(p => p.Pid == Environment.ProcessId).ShouldNotBeNull();
        self.ExeName.ShouldNotBeNullOrWhiteSpace();
        self.ExeName.ShouldEndWith(".exe", Case.Insensitive);
        self.ParentPid.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Snapshot_is_repeatable_and_does_not_leak_handles()
    {
        WindowsOnly.Require();
        var source = new ProcessSource();
        var before = System.Diagnostics.Process.GetCurrentProcess().HandleCount;

        for (var i = 0; i < 200; i++)
        {
            source.Snapshot();
        }

        System.Diagnostics.Process.GetCurrentProcess().HandleCount.ShouldBeLessThan(before + 50);
    }

    [Fact]
    public void Image_path_of_the_current_process_resolves()
    {
        WindowsOnly.Require();

        var path = new ProcessDetailsSource().TryGetImagePath(Environment.ProcessId);

        path.ShouldNotBeNull();
        File.Exists(path).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(999_999)]
    public void Protected_or_missing_processes_return_null_instead_of_throwing(int pid)
    {
        WindowsOnly.Require();

        Should.NotThrow(() => new ProcessDetailsSource().TryGetImagePath(pid));
    }

    [Fact]
    public void Window_title_lookup_for_a_process_without_windows_is_null_not_an_error()
    {
        WindowsOnly.Require();

        Should.NotThrow(() => new ProcessDetailsSource().TryGetMainWindowTitle(Environment.ProcessId));
        new ProcessDetailsSource().TryGetMainWindowTitle(999_999).ShouldBeNull();
    }
}
