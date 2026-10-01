using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Policy;

/// <summary>
/// Static guards for the non-negotiable rules in CLAUDE.md / SPEC. They read the source tree, so they run on Linux CI and
/// keep protecting the Windows-only code that cannot execute there.
/// </summary>
public partial class SourceGuardTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrettyDesk.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static IEnumerable<(string Path, string Text)> Sources(string project) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src", project), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (f, File.ReadAllText(f)));

    private static IEnumerable<(string Path, string Text)> AllProductSources() =>
        new[] { "PrettyDesk.Core", "PrettyDesk.Windows", "PrettyDesk.App" }
            .Where(p => Directory.Exists(Path.Combine(Root, "src", p)))
            .SelectMany(Sources);

    [GeneratedRegex(@"\bOpenProcess\s*\(([^;]*)\)")]
    private static partial Regex OpenProcessCall();

    [Fact]
    public void Process_handles_are_only_ever_opened_with_query_limited_information()
    {
        var calls = AllProductSources().SelectMany(s => OpenProcessCall().Matches(s.Text).Select(m => (s.Path, Args: m.Groups[1].Value))).ToList();

        calls.ShouldNotBeEmpty("the image-path lookup is expected to exist");
        foreach (var (path, args) in calls)
        {
            args.ShouldContain("PROCESS_QUERY_LIMITED_INFORMATION", Case.Sensitive, $"{path}: FR-DET-3 allows only PROCESS_QUERY_LIMITED_INFORMATION");
            args.ShouldNotContain("|", Case.Sensitive, $"{path}: access rights must not be combined");
        }
    }

    [Theory]
    [InlineData("ReadProcessMemory")]
    [InlineData("WriteProcessMemory")]
    [InlineData("VirtualAllocEx")]
    [InlineData("VirtualProtectEx")]
    [InlineData("CreateRemoteThread")]
    [InlineData("NtCreateThreadEx")]
    [InlineData("SetWindowsHookEx")]
    [InlineData("EnumProcessModules")]
    [InlineData("Module32First")]
    [InlineData("TH32CS_SNAPMODULE")]
    [InlineData("PROCESS_VM_READ")]
    [InlineData("PROCESS_VM_WRITE")]
    [InlineData("PROCESS_VM_OPERATION")]
    [InlineData("PROCESS_ALL_ACCESS")]
    [InlineData("PROCESS_CREATE_THREAD")]
    [InlineData("PROCESS_DUP_HANDLE")]
    [InlineData("DebugActiveProcess")]
    [InlineData("LoadLibraryEx")]
    [InlineData("Process.GetProcesses")]
    [InlineData("Process.GetProcessById")]
    [InlineData("MainModule")]
    public void Anti_cheat_unsafe_apis_are_never_referenced(string forbidden)
    {
        foreach (var (path, text) in AllProductSources())
        {
            // Allow mentions in comments only; code must not call them.
            var code = string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));
            code.ShouldNotContain(forbidden, Case.Sensitive, $"{path} references {forbidden} (FR-DET-3)");
        }

        var natives = File.ReadAllLines(Path.Combine(Root, "src", "PrettyDesk.Windows", "NativeMethods.txt"));
        natives.ShouldNotContain(forbidden);
    }

    [Fact]
    public void Native_method_list_stays_within_the_reviewed_surface()
    {
        var natives = File.ReadAllLines(Path.Combine(Root, "src", "PrettyDesk.Windows", "NativeMethods.txt")).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        var reviewed = new HashSet<string> { "OpenProcess", "PROCESS_ACCESS_RIGHTS", "QueryFullProcessImageNameW", "PROCESSENTRY32W", "GetWindowThreadProcessId", "Process32FirstW", "Process32NextW" };
        var processRelated = natives.Where(n => n.Contains("Process", StringComparison.Ordinal)).ToList();

        processRelated.Where(n => !reviewed.Contains(n)).ShouldBeEmpty("a new process-related API needs an FR-DET-3 review");
    }

    [Fact]
    public void Nothing_ever_writes_to_HKLM_or_Program_Files()
    {
        foreach (var (path, text) in AllProductSources())
        {
            text.ShouldNotContain("LocalMachine.CreateSubKey", Case.Sensitive, path);
            text.ShouldNotContain("LocalMachine.DeleteSubKey", Case.Sensitive, path);
            Regex.IsMatch(text, @"LocalMachine\.OpenSubKey\([^)]*(true|writable: ?true)").ShouldBeFalse($"{path} opens an HKLM key writable");
            Regex.IsMatch(text, @"SpecialFolder\.ProgramFiles").ShouldBeFalse($"{path} references Program Files");
        }
    }

    [Fact]
    public void JPEG_import_quality_is_never_touched()
    {
        foreach (var (path, text) in AllProductSources())
        {
            text.ShouldNotContain("JPEGImportQuality", Case.Insensitive, path);
        }
    }

    [Fact]
    public void Core_uses_time_provider_never_the_wall_clock_or_real_sleeps()
    {
        foreach (var (path, text) in Sources("PrettyDesk.Core"))
        {
            text.ShouldNotContain("DateTime.Now", Case.Sensitive, path);
            text.ShouldNotContain("Thread.Sleep", Case.Sensitive, path);
            foreach (Match match in Regex.Matches(text, @"Task\.Delay\(([^;]*)\)"))
            {
                match.Groups[1].Value.ShouldContain("_time", Case.Sensitive, $"{path}: Task.Delay must use the injected TimeProvider");
            }
        }
    }

    [Fact]
    public void Core_has_no_windows_dependencies()
    {
        var project = File.ReadAllText(Path.Combine(Root, "src", "PrettyDesk.Core", "PrettyDesk.Core.csproj"));

        project.ShouldContain("<TargetFramework>net10.0</TargetFramework>");
        project.ShouldNotContain("windows", Case.Insensitive);
        foreach (var (path, text) in Sources("PrettyDesk.Core"))
        {
            text.ShouldNotContain("Windows.Win32", Case.Sensitive, path);
            text.ShouldNotContain("Microsoft.Win32", Case.Sensitive, path);
            text.ShouldNotContain("DllImport", Case.Sensitive, path);
        }
    }

    [Fact]
    public void No_telemetry_or_unexpected_network_hosts_in_product_code()
    {
        foreach (var (path, text) in AllProductSources())
        {
            text.ShouldNotContain("ApplicationInsights", Case.Insensitive, path);
            text.ShouldNotContain("Sentry", Case.Sensitive, path);
            text.ShouldNotContain("Analytics", Case.Sensitive, path);
        }
    }

    [Fact]
    public void Product_code_has_no_unfinished_markers_or_not_implemented_stubs()
    {
        foreach (var (path, text) in AllProductSources())
        {
            text.ShouldNotContain("NotImplementedException", Case.Sensitive, path);
            Regex.IsMatch(text, @"//\s*TODO").ShouldBeFalse($"{path} has a TODO; deferred work belongs in docs/BACKLOG.md");
        }
    }
}
