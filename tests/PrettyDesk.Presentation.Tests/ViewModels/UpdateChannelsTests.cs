using System.Runtime.InteropServices;
using PrettyDesk.Presentation.Services;
using Shouldly;
using Xunit;

namespace PrettyDesk.Presentation.Tests.ViewModels;

public sealed class UpdateChannelsTests
{
    [Theory]
    [InlineData(false, Architecture.X64, "win-x64-stable")]
    [InlineData(true, Architecture.X64, "win-x64-beta")]
    [InlineData(false, Architecture.Arm64, "win-arm64-stable")]
    [InlineData(true, Architecture.Arm64, "win-arm64-beta")]
    public void Channel_names_match_what_the_release_workflow_packs(bool beta, Architecture architecture, string expected)
    {
        UpdateChannels.For(beta, architecture).ShouldBe(expected);
    }

    [Fact]
    public void The_release_workflow_and_the_app_agree_on_the_channel_naming()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRoot(), ".github", "workflows", "release.yml"));

        workflow.ShouldContain("$channel = \"${{ matrix.rid }}-\" + ($(if ($stable) { 'stable' } else { 'beta' }))");
        workflow.ShouldContain("rid: win-x64");
        workflow.ShouldContain("rid: win-arm64");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrettyDesk.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
