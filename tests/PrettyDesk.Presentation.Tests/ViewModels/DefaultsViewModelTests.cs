using NSubstitute;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Presentation.Tests.Support;
using PrettyDesk.Presentation.ViewModels;
using Shouldly;
using Xunit;

namespace PrettyDesk.Presentation.Tests.ViewModels;

public class DefaultsViewModelTests
{
    private readonly LibraryFixture _f = new();
    private readonly IAppController _app = Substitute.For<IAppController>();

    public DefaultsViewModelTests()
    {
        _f.Catalog.Current = new CatalogDocument
        {
            Collections =
            [
                new CollectionEntry { Id = "default.matte-black", PackId = "default.matte-black", Title = "Matte Black", Order = 1 },
                new CollectionEntry { Id = "default.clean-white", PackId = "default.clean-white", Title = "Clean White", Order = 2 },
            ],
            Packs =
            [
                new PackEntry { Id = "default.matte-black", Wallpapers = [new WallpaperEntry { Id = "mb-01", Title = "Obsidian Dune", Starter = true }, new WallpaperEntry { Id = "mb-02", Title = "Basalt" }] },
                new PackEntry { Id = "default.clean-white", Wallpapers = [new WallpaperEntry { Id = "cw-01", Title = "Leaf Shadows", Starter = true }] },
            ],
        };
        _f.Library.Packs["default.matte-black"] = ["mb-01", "mb-02"];
        _f.Library.Packs["default.clean-white"] = ["cw-01"];
        foreach (var id in new[] { "mb-01", "mb-02", "cw-01" })
        {
            _f.Library.Available.Add(id);
            _f.Content.Previews[id] = "/t/" + id + ".jpg";
        }
    }

    private DefaultsViewModel Vm() => new(_f.Settings, _f.Catalog, _f.Content, _f.Library, _f.Monitors, _f.Files, _app, _f.Ui);

    [Fact]
    public void Collections_are_listed_in_catalog_order_with_selection_counts_and_previews()
    {
        var vm = Vm();

        vm.Collections.Select(c => c.Title).ShouldBe(["Matte Black", "Clean White"]);
        vm.Collections[0].IsSelected.ShouldBeTrue();
        vm.Collections[1].IsSelected.ShouldBeFalse();
        vm.Collections[0].CountText.ShouldBe("2 wallpapers");
        vm.Collections[0].PreviewPath.ShouldBe("/t/mb-01.jpg");
        vm.Collections[0].ToggleLabel.ShouldBe("Use Matte Black");
        vm.HasCollections.ShouldBeTrue();
    }

    [Fact]
    public void Selecting_and_deselecting_collections_persists()
    {
        var vm = Vm();

        vm.Collections[1].IsSelected = true;
        _f.Settings.Current.Default.Selection.Collections.ShouldBe(["default.matte-black", "default.clean-white"]);

        vm.Collections[0].IsSelected = false;
        _f.Settings.Current.Default.Selection.Collections.ShouldBe(["default.clean-white"]);
    }

    [Fact]
    public void The_last_selection_cannot_be_turned_off()
    {
        var vm = Vm();

        vm.Collections[0].IsSelected = false;

        vm.Collections[0].IsSelected.ShouldBeTrue();
        vm.Notice.ShouldBe("Keep at least one collection or image turned on.");
        _f.Settings.Current.Default.Selection.Collections.ShouldBe(["default.matte-black"]);
    }

    [Fact]
    public void A_surprise_me_selection_of_individual_wallpapers_counts_as_a_selection()
    {
        _f.Settings.Update(s =>
        {
            s.Default.Selection.Collections = [];
            s.Default.Selection.Wallpapers = ["mb-01", "cw-01"];
        });
        var vm = Vm();
        vm.Collections.ShouldAllBe(c => !c.IsSelected);

        vm.Collections[1].IsSelected = true;
        vm.Collections[1].IsSelected = false;

        vm.Notice.ShouldBeNull();
    }

    [Fact]
    public void Empty_catalog_shows_the_designed_empty_state_flag()
    {
        _f.Catalog.Current = new CatalogDocument();

        Vm().HasCollections.ShouldBeFalse();
    }

    [Fact]
    public void Mode_order_theme_and_battery_options_persist()
    {
        var vm = Vm();

        vm.IsShuffle = false;
        vm.FollowTheme = true;
        vm.PauseOnBatterySaver = false;

        _f.Settings.Current.Default.Order.ShouldBe(RotationOrder.Sequential);
        _f.Settings.Current.Default.FollowWindowsTheme.ShouldBeTrue();
        _f.Settings.Current.Default.PauseRotationOnBatterySaver.ShouldBeFalse();
    }

    [Fact]
    public void Fixed_mode_picks_a_wallpaper_from_the_selection_and_lets_the_user_change_it()
    {
        var vm = Vm();

        vm.IsRotate = false;

        _f.Settings.Current.Default.Mode.ShouldBe(WallpaperMode.Fixed);
        _f.Settings.Current.Default.FixedWallpaperId.ShouldBe("mb-01");
        vm.ShowFixedPicker.ShouldBeTrue();
        vm.FixedCandidates.Select(c => c.Id).ShouldBe(["mb-01", "mb-02"]);

        vm.FixedCandidates[1].IsFavorite = true;

        _f.Settings.Current.Default.FixedWallpaperId.ShouldBe("mb-02");
        vm.FixedCandidates[0].IsFavorite.ShouldBeFalse();
    }

    [Fact]
    public void Unstarring_the_fixed_wallpaper_goes_back_to_rotating()
    {
        var vm = Vm();
        vm.IsRotate = false;
        vm.FixedCandidates[0].IsFavorite.ShouldBeTrue();

        vm.FixedCandidates[0].IsFavorite = false;

        _f.Settings.Current.Default.Mode.ShouldBe(WallpaperMode.Rotate);
        vm.IsRotate.ShouldBeTrue();
    }

    [Fact]
    public void Fixed_mode_without_any_downloaded_wallpaper_explains_what_to_do()
    {
        _f.Library.Available.Clear();
        var vm = Vm();

        vm.IsRotate = false;

        vm.Notice.ShouldBe("Pick a collection first, then choose your wallpaper.");
        vm.HasFixedCandidates.ShouldBeFalse();
    }

    [Fact]
    public void Interval_presets_persist_and_default_to_thirty_minutes()
    {
        var vm = Vm();
        vm.SelectedInterval!.Label.ShouldBe("Every 30 minutes");
        vm.IntervalChoices.ShouldNotContain(c => c.Value.Kind == RotationIntervalKind.Session);

        vm.SelectedInterval = vm.IntervalChoices.First(c => c.Value == RotationInterval.Every(TimeSpan.FromHours(3)));

        _f.Settings.Current.Default.Interval.ShouldBe(RotationInterval.Every(TimeSpan.FromHours(3)));
        vm.IsCustomInterval.ShouldBeFalse();
    }

    [Fact]
    public void Unlock_is_an_available_interval()
    {
        var vm = Vm();

        vm.SelectedInterval = vm.IntervalChoices.First(c => c.Value.Kind == RotationIntervalKind.Unlock);

        _f.Settings.Current.Default.Interval.Kind.ShouldBe(RotationIntervalKind.Unlock);
    }

    [Fact]
    public void Custom_interval_is_clamped_between_one_minute_and_seven_days()
    {
        var vm = Vm();
        vm.SelectedInterval = vm.IntervalChoices[^1];
        vm.IsCustomInterval.ShouldBeTrue();

        vm.CustomMinutes = 45;
        _f.Settings.Current.Default.Interval.Duration.ShouldBe(TimeSpan.FromMinutes(45));

        vm.CustomMinutes = 999_999;
        vm.CustomMinutes.ShouldBe(DefaultsViewModel.MaxCustomMinutes);
        _f.Settings.Current.Default.Interval.Duration.ShouldBe(TimeSpan.FromDays(7));

        vm.CustomMinutes = 0;
        _f.Settings.Current.Default.Interval.Duration.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void A_non_preset_stored_interval_loads_as_custom()
    {
        _f.Settings.Update(s => s.Default.Interval = RotationInterval.Every(TimeSpan.FromMinutes(47)));

        var vm = Vm();

        vm.IsCustomInterval.ShouldBeTrue();
        vm.CustomMinutes.ShouldBe(47);
    }

    [Fact]
    public async Task Importing_images_adds_them_turns_my_images_on_and_reports_the_result()
    {
        _f.Monitors.Monitors = [new MonitorInfo("m", 0, 0, 1600, 900, true)];
        var vm = Vm();

        await vm.ImportAsync(["/p/a.png", "/p/b.png"]);

        vm.MyImages.Select(i => i.Name).ShouldBe(["a.png", "b.png"]);
        vm.HasImages.ShouldBeTrue();
        vm.UseMyImages.ShouldBeTrue();
        _f.Settings.Current.Default.Selection.Collections.ShouldContain("user");
        vm.ImportMessage.ShouldBe("2 image(s) added.");
    }

    [Fact]
    public async Task Skipped_images_are_explained_in_human_words()
    {
        _f.Content.NextImport = new ImportResult(null, ImportFailure.TooSmall, "That image is 100×100.");
        var vm = Vm();

        await vm.ImportAsync(["/p/tiny.png"]);

        vm.ImportMessage.ShouldBe("1 image(s) couldn't be added. That image is 100×100.");
        vm.MyImages.ShouldBeEmpty();
        vm.UseMyImages.ShouldBeFalse();
        vm.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task Images_smaller_than_the_display_carry_a_soft_warning()
    {
        _f.Monitors.Monitors = [new MonitorInfo("m", 0, 0, 3840, 2160, true)];
        var vm = Vm();

        await vm.ImportAsync(["/p/a.png"]);

        vm.MyImages[0].Warning.ShouldNotBeNull().ShouldContain("may look soft");
        vm.ImportMessage.ShouldNotBeNull().ShouldContain("may look soft");
    }

    [Fact]
    public async Task Empty_drops_are_ignored()
    {
        var vm = Vm();

        await vm.ImportAsync([]);

        _f.Content.ImportCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Add_images_uses_the_file_picker()
    {
        _f.Files.Images = ["/p/pick.png"];
        var vm = Vm();

        await vm.AddImagesCommand.ExecuteAsync(null);

        vm.MyImages.ShouldHaveSingleItem().Name.ShouldBe("pick.png");
    }

    [Fact]
    public async Task Removing_an_image_updates_the_list_and_clears_a_fixed_wallpaper_pointing_at_it()
    {
        var vm = Vm();
        await vm.ImportAsync(["/p/a.png"]);
        _f.Settings.Update(s => s.Default.FixedWallpaperId = "user:a.png");

        vm.RemoveImageCommand.Execute(vm.MyImages[0]);

        vm.MyImages.ShouldBeEmpty();
        _f.Settings.Current.Default.FixedWallpaperId.ShouldBeNull();
        vm.MyImages.Count.ShouldBe(0);
    }

    [Fact]
    public async Task My_images_cannot_be_turned_off_when_it_is_the_only_selection()
    {
        _f.Settings.Update(s => s.Default.Selection.Collections = ["user"]);
        _f.Content.Images.Add(new UserImage("user:a.png", "/p/a.png", 3000, 2000));
        var vm = Vm();
        vm.UseMyImages.ShouldBeTrue();

        vm.UseMyImages = false;

        vm.UseMyImages.ShouldBeTrue();
        vm.Notice.ShouldBe("Keep at least one collection or image turned on.");
        await Task.CompletedTask;
    }

    [Fact]
    public void My_images_can_be_turned_off_when_a_collection_remains()
    {
        _f.Settings.Update(s => s.Default.Selection.Collections = ["default.matte-black", "user"]);
        var vm = Vm();

        vm.UseMyImages = false;

        _f.Settings.Current.Default.Selection.Collections.ShouldBe(["default.matte-black"]);
    }

    [Fact]
    public void Rerun_setup_opens_the_onboarding_quiz()
    {
        Vm().RerunSetupCommand.Execute(null);

        _app.Received(1).ShowOnboarding();
    }

    [Fact]
    public void Dark_only_counts_and_offers_only_dark_wallpapers()
    {
        _f.Library.ToneById["mb-02"] = Tones.Light;
        var vm = Vm();

        vm.DarkOnly = true;

        _f.Settings.Current.General.DarkWallpapersOnly.ShouldBeTrue();
        vm.Collections[0].Count.ShouldBe(1, "the light Basalt wallpaper is hidden from the collection");
        vm.Collections[0].PreviewPath.ShouldBe("/t/mb-01.jpg");
        vm.FixedCandidates.Select(c => c.Id).ShouldBe(["mb-01"]);
        vm.DarkOnlyWarning.ShouldBeNull();
    }

    [Fact]
    public void Dark_only_with_only_light_collections_warns_instead_of_silently_showing_nothing()
    {
        _f.Library.ToneById["cw-01"] = Tones.Light;
        _f.Settings.Update(s => s.Default.Selection.Collections = ["default.clean-white"]);
        var vm = Vm();

        vm.DarkOnly = true;

        vm.DarkOnlyWarning.ShouldBe("None of your selected wallpapers is dark. Choose a dark collection, such as Matte Black, or turn this option off.");
        vm.FixedCandidates.ShouldBeEmpty();

        vm.DarkOnly = false;

        vm.DarkOnlyWarning.ShouldBeNull();
        vm.FixedCandidates.Select(c => c.Id).ShouldBe(["cw-01"]);
    }

    [Fact]
    public void The_dark_only_choice_is_remembered_between_visits()
    {
        _f.Settings.Update(s => s.General.DarkWallpapersOnly = true);

        Vm().DarkOnly.ShouldBeTrue();
    }

    [Fact]
    public void Catalog_updates_refresh_the_collection_list()
    {
        var vm = Vm();
        _f.Catalog.Current = new CatalogDocument { Collections = [new CollectionEntry { Id = "default.new", PackId = "default.new", Title = "New One" }], Packs = [new PackEntry { Id = "default.new" }] };

        _f.Catalog.Raise();

        vm.Collections.ShouldHaveSingleItem().Title.ShouldBe("New One");
    }
}
