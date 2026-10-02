using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Orchestration;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Catalog;

/// <summary>
/// Guards the shipped seed catalog (content/catalog.src.json): structurally valid, never matches a launcher or anti-cheat
/// service (docs/GAME_CATALOG_SEED.md rule 4), and stays in step with the art prompt files and the onboarding quiz.
/// </summary>
public sealed class CatalogSourceTests
{
    private static readonly string Root = FindRoot();

    private static readonly Lazy<CatalogDocument> Source = new(Load);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrettyDesk.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static CatalogDocument Load()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Root, "content", "catalog.src.json"));
        var check = CatalogValidator.ParseAndValidate(bytes, new Version(1, 0, 0));
        check.Rejection.ShouldBe(CatalogRejection.None, check.Detail);
        return check.Document!;
    }

    [Fact]
    public void Parses_and_validates_for_app_1_0_0()
    {
        Source.Value.Games.Count.ShouldBeGreaterThanOrEqualTo(45);
        Source.Value.SchemaVersion.ShouldBe(CatalogDocument.CurrentSchemaVersion);
        Source.Value.Disclaimer.ShouldContain("not affiliated");
    }

    [Fact]
    public void No_game_rule_matches_a_launcher_or_anti_cheat_service()
    {
        var excluded = Source.Value.ExcludeExeNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        excluded.ShouldContain("steam.exe");
        excluded.ShouldContain("vgc.exe");
        excluded.ShouldContain("EasyAntiCheat.exe");

        foreach (var game in Source.Value.Games)
        {
            foreach (var exe in game.Detection.ExeNames.Where(excluded.Contains))
            {
                // javaw.exe is the one allowed exception, and only when paired with a window-title rule (RuleMatcher).
                exe.ShouldBe("javaw.exe", $"{game.Id} lists the excluded launcher/helper '{exe}'");
                game.Detection.WindowTitleContains.ShouldNotBeEmpty($"{game.Id}: javaw.exe is ambiguous and needs a title rule");
            }
        }
    }

    [Fact]
    public void Exe_names_and_steam_ids_identify_one_game_unless_the_art_pack_is_shared()
    {
        var byExe = Source.Value.Games
            .SelectMany(g => g.Detection.ExeNames.Select(e => (Exe: e.ToLowerInvariant(), Game: g)))
            .GroupBy(x => x.Exe)
            .Where(g => g.Count() > 1);
        byExe.ShouldBeEmpty("an exe name claimed by two games would flip wallpapers: " + string.Join(", ", byExe.Select(g => g.Key)));

        var byApp = Source.Value.Games
            .SelectMany(g => g.Detection.SteamAppIds.Select(a => (App: a, Game: g)))
            .GroupBy(x => x.App)
            .Where(g => g.Count() > 1);
        byApp.ShouldBeEmpty("a Steam AppID claimed by two games");
    }

    [Fact]
    public void Every_game_has_its_art_pack_and_every_art_prompt_file_has_a_pack()
    {
        var packIds = Source.Value.Packs.Select(p => p.Id).ToHashSet();
        foreach (var game in Source.Value.Games)
        {
            packIds.ShouldContain(game.PackId, $"{game.Id} has no pack");
        }

        var files = Directory.GetFiles(Path.Combine(Root, "art", "prompts"), "*.yaml").Select(Path.GetFileNameWithoutExtension).ToList();
        files.ShouldNotBeEmpty();
        foreach (var file in files)
        {
            packIds.ShouldContain(file!, $"art/prompts/{file}.yaml has no pack in catalog.src.json");
        }

        Source.Value.Packs.Select(p => p.Id).ShouldBe(Source.Value.Packs.Select(p => p.Id).Distinct());
        Source.Value.Packs.Count.ShouldBe(files.Count, "every pack in the catalog is backed by a prompt file");
    }

    [Fact]
    public void Default_collections_include_the_quiz_styles_and_the_Liquid_Glass_capsule()
    {
        var collections = Source.Value.Collections;
        collections.Count.ShouldBe(14);
        collections.Select(c => c.Order).ShouldBe(Enumerable.Range(1, 14));
        collections[0].Id.ShouldBe("default.ios-glass");

        foreach (var collection in collections)
        {
            Source.Value.FindPack(collection.PackId)!.Kind.ShouldBe("default");
            collection.SetupMatch.ShouldNotBeEmpty();
        }

        var ids = collections.Select(c => c.Id).ToHashSet();
        foreach (var style in Enum.GetValues<SetupStyle>())
        {
            foreach (var id in SetupStyles.CollectionsFor(style))
            {
                ids.ShouldContain(id, $"the {style} quiz answer references a collection missing from the catalog");
            }
        }
    }

    [Fact]
    public void Seed_games_start_unverified_so_the_owner_confirms_them_in_the_detection_log()
    {
        Source.Value.Games.ShouldAllBe(g => g.Verified == null);
    }
}
