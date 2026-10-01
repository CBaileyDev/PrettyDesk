using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Core.Settings;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Orchestration;

public class SetupStylesTests
{
    private static readonly string[] AllCollections =
    [
        "matte-black", "clean-white", "warm-minimal", "sage-botanical", "soft-gradients", "misty-nature", "painted",
        "steel-blue-night", "cozy-lofi", "deep-space", "pastel", "neon-minimal", "architectural",
    ];

    private static CatalogDocument Catalog()
    {
        var packs = AllCollections.Select(name => new PackEntry
        {
            Id = "default." + name,
            Kind = "default",
            Wallpapers =
            [
                new WallpaperEntry { Id = name + "-dark", Tone = Tones.Dark },
                new WallpaperEntry { Id = name + "-light", Tone = Tones.Light },
                new WallpaperEntry { Id = name + "-mid", Tone = Tones.Mid, Starter = true },
            ],
        }).ToList();
        return new CatalogDocument
        {
            Packs = packs,
            Collections = AllCollections.Select((name, i) => new CollectionEntry { Id = "default." + name, PackId = "default." + name, Order = i + 1 }).ToList(),
        };
    }

    [Theory]
    [InlineData(SetupStyle.MatteBlack, new[] { "default.matte-black", "default.deep-space", "default.steel-blue-night", "default.architectural" })]
    [InlineData(SetupStyle.CleanWhite, new[] { "default.clean-white", "default.soft-gradients", "default.architectural", "default.misty-nature" })]
    [InlineData(SetupStyle.WarmWoodAndPlants, new[] { "default.warm-minimal", "default.sage-botanical", "default.painted", "default.cozy-lofi" })]
    [InlineData(SetupStyle.Pastel, new[] { "default.pastel", "default.soft-gradients", "default.cozy-lofi" })]
    [InlineData(SetupStyle.Rgb, new[] { "default.neon-minimal", "default.deep-space", "default.steel-blue-night" })]
    public void Each_style_selects_the_collections_from_the_art_direction_table(SetupStyle style, string[] expected)
    {
        var selection = new SelectionSettings();

        SetupStyles.Apply(style, Catalog(), selection);

        selection.Collections.ShouldBe(expected);
        SetupStyles.CollectionsFor(style).ShouldBe(expected);
    }

    [Fact]
    public void Matte_black_excludes_light_architecture_but_keeps_dark_and_mid()
    {
        var selection = new SelectionSettings();

        SetupStyles.Apply(SetupStyle.MatteBlack, Catalog(), selection);

        selection.Excluded.ShouldBe(["architectural-light"]);
    }

    [Fact]
    public void Clean_white_restricts_three_collections_to_light()
    {
        var selection = new SelectionSettings();

        SetupStyles.Apply(SetupStyle.CleanWhite, Catalog(), selection);

        selection.Excluded.ShouldBe(["soft-gradients-dark", "architectural-dark", "misty-nature-dark"], ignoreOrder: true);
    }

    [Fact]
    public void Surprise_me_picks_the_starter_wallpaper_of_every_collection()
    {
        var selection = new SelectionSettings { Collections = ["x"], Excluded = ["y"] };

        SetupStyles.Apply(SetupStyle.SurpriseMe, Catalog(), selection);

        selection.Collections.ShouldBeEmpty();
        selection.Excluded.ShouldBeEmpty();
        selection.Wallpapers.Count.ShouldBe(13);
        selection.Wallpapers.ShouldAllBe(id => id.EndsWith("-mid", StringComparison.Ordinal));
        SetupStyles.CollectionsFor(SetupStyle.SurpriseMe).ShouldBeEmpty();
    }

    [Fact]
    public void Collections_missing_from_a_partial_catalog_are_skipped()
    {
        var partial = new CatalogDocument
        {
            Collections = [new CollectionEntry { Id = "default.matte-black", PackId = "default.matte-black" }],
            Packs = [new PackEntry { Id = "default.matte-black" }],
        };
        var selection = new SelectionSettings();

        SetupStyles.Apply(SetupStyle.MatteBlack, partial, selection);

        selection.Collections.ShouldBe(["default.matte-black"]);
    }

    [Fact]
    public void Applying_a_style_replaces_the_previous_selection()
    {
        var selection = new SelectionSettings { Collections = ["default.old"], Wallpapers = ["w"], Excluded = ["e"] };

        SetupStyles.Apply(SetupStyle.Pastel, Catalog(), selection);

        selection.Collections.ShouldNotContain("default.old");
        selection.Wallpapers.ShouldBeEmpty();
        selection.Excluded.ShouldBeEmpty();
    }
}
