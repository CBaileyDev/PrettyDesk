using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Catalog;

public sealed class ToneFilterTests
{
    [Theory]
    [InlineData(Tones.Dark, true, true)]
    [InlineData(Tones.Mid, true, false)]
    [InlineData(Tones.Light, true, false)]
    [InlineData(null, true, false)]
    [InlineData(Tones.Dark, false, true)]
    [InlineData(Tones.Light, false, true)]
    [InlineData(Tones.Mid, false, true)]
    public void Dark_only_shows_dark_wallpapers_and_nothing_hides_when_it_is_off(string? tone, bool darkOnly, bool expected)
    {
        ToneFilter.Allows(tone, darkOnly).ShouldBe(expected);
    }

    [Fact]
    public void Tone_comes_from_the_local_asset_when_the_file_is_present()
    {
        var content = new FakeContent();
        content.MakeAvailable("p", "x", Tones.Light);

        ToneFilter.ToneOf(Catalog("p", "x", Tones.Dark), content, "x").ShouldBe(Tones.Light);
    }

    [Fact]
    public void Tone_falls_back_to_the_catalog_for_wallpapers_that_are_not_downloaded_yet()
    {
        var content = new FakeContent();
        content.AddPack("p", Tones.Light, downloaded: false, "x");

        ToneFilter.ToneOf(Catalog("p", "x", Tones.Dark), content, "x").ShouldBe(Tones.Dark);
    }

    [Fact]
    public void Unknown_wallpapers_have_no_tone()
    {
        ToneFilter.ToneOf(CatalogDocument.Empty, new FakeContent(), "missing").ShouldBeNull();
    }

    [Theory]
    [InlineData(Tones.Dark, true, true)]
    [InlineData(Tones.Light, true, false)]
    [InlineData(Tones.Light, false, true)]
    public void A_wallpaper_id_is_judged_by_its_catalog_tone_when_not_downloaded(string tone, bool darkOnly, bool expected)
    {
        ToneFilter.Allows(Catalog("p", "x", tone), new FakeContent(), "x", darkOnly).ShouldBe(expected);
    }

    [Fact]
    public void An_unknown_wallpaper_id_is_hidden_by_dark_only_but_shown_otherwise()
    {
        ToneFilter.Allows(CatalogDocument.Empty, new FakeContent(), "missing", darkOnly: true).ShouldBeFalse();
        ToneFilter.Allows(CatalogDocument.Empty, new FakeContent(), "missing", darkOnly: false).ShouldBeTrue();
    }

    private static CatalogDocument Catalog(string packId, string wallpaperId, string tone) => new()
    {
        Packs = [new PackEntry { Id = packId, Kind = "default", Wallpapers = [new WallpaperEntry { Id = wallpaperId, Tone = tone }] }],
    };
}
