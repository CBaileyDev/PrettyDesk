using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Detection.Discovery;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Detection;

public sealed class DeclaredInstallScannerTests
{
    [Fact]
    public void Stale_records_and_similar_titles_do_not_confirm_an_install()
    {
        using var dir = new TempDir();
        var catalog = new CatalogDocument { Games = [new GameEntry { Id = "fh5", DisplayName = "Forza Horizon 5", Detection = new DetectionRules { ExeNames = ["Forza.exe"] } }] };
        File.WriteAllText(dir.File("Forza.exe"), "fixture");
        DeclaredInstallScanner.Scan([new("Forza Horizon 6", dir.Path), new("Forza Horizon 5", dir.File("missing"))], catalog).ShouldBeEmpty();
        DeclaredInstallScanner.Scan([new("Forza Horizon 5", dir.Path), new("Forza Horizon 5", dir.Path)], catalog).Count.ShouldBe(1);
    }

    [Fact]
    public void Icon_only_directories_are_not_installs_but_nested_game_executables_are()
    {
        using var dir = new TempDir();
        var catalog = new CatalogDocument { Games = [new GameEntry { Id = "g", DisplayName = "Game", Detection = new DetectionRules { ExeNames = ["game.exe"] } }] };
        var declared = new[] { new DeclaredInstallation("Game", dir.Path) };
        File.WriteAllText(dir.File("game.ico"), "fixture");
        DeclaredInstallScanner.Scan(declared, catalog).ShouldBeEmpty();
        Directory.CreateDirectory(dir.File("bin"));
        File.WriteAllText(dir.File("bin/game.exe"), "fixture");
        DeclaredInstallScanner.Scan(declared, catalog).Single().ExeHint.ShouldNotBeNull();
    }
}
