using System.Globalization;
using NSubstitute;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Content;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;
using PrettyDesk.Presentation.Tests.Support;
using PrettyDesk.Presentation.ViewModels;
using Shouldly;
using Xunit;

namespace PrettyDesk.Presentation.Tests.ViewModels;

internal sealed class LibraryFixture
{
    public FakeSettingsProvider Settings { get; } = new();
    public FakeContentBrowser Content { get; } = new();
    public FakeLibrary Library { get; } = new();
    public FakeController Controller { get; } = new();
    public FakeMonitorProvider Monitors { get; } = new();
    public FakeFilePicker Files { get; } = new();
    public FakeRunningApps Running { get; } = new();
    public FakeInstalled Installed { get; } = new();
    public IDialogService Dialogs { get; } = Substitute.For<IDialogService>();
    public IExternalLauncher Launcher { get; } = Substitute.For<IExternalLauncher>();
    public InlineDispatcher Ui { get; } = new();
    public FakeCatalogProvider Catalog { get; }
    public int RemovedCallbacks { get; private set; }

    public LibraryFixture()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en-US");
        Dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(true);
        Catalog = new FakeCatalogProvider(new CatalogDocument
        {
            ExcludeExeNames = ["steam.exe", "EpicGamesLauncher.exe"],
            Games =
            [
                new GameEntry { Id = "valorant", DisplayName = "VALORANT", PackId = "game.valorant", Detection = new DetectionRules { ExeNames = ["VALORANT-Win64-Shipping.exe"] } },
                new GameEntry { Id = "cs2", DisplayName = "Counter-Strike 2", PackId = "game.cs2", Detection = new DetectionRules { ExeNames = ["cs2.exe"], SteamAppIds = [730] } },
                new GameEntry { Id = "apex", DisplayName = "Apex Legends", PackId = "game.apex", Detection = new DetectionRules { ExeNames = ["r5apex.exe"] } },
            ],
            Packs =
            [
                Pack("game.valorant", "val-hero", "val-min", "val-mood"),
                Pack("game.cs2", "cs2-hero", "cs2-min", "cs2-mood"),
                Pack("game.apex", "apex-hero"),
            ],
        });
        foreach (var pack in Catalog.Current.Packs)
        {
            Library.Packs[pack.Id] = pack.Wallpapers.Select(w => w.Id).ToList();
        }
    }

    private static PackEntry Pack(string id, params string[] wallpapers) => new()
    {
        Id = id,
        Title = id,
        Wallpapers = wallpapers.Select((w, i) => new WallpaperEntry { Id = w, Title = "Title " + w, Role = i == 0 ? "hero" : "mood" }).ToList(),
    };

    public WallpaperPickerViewModel NewPicker(IEnumerable<string> existing) => new(Catalog.Current, Library, Content, Files, existing);

    public GameDetailViewModel NewDetail(GameListing listing, Action? onRemoved = null) => new(
        listing, Settings, Catalog, Content, Library, Controller, Monitors, Dialogs, Files, Launcher, Ui, NewPicker, () =>
        {
            RemovedCallbacks++;
            onRemoved?.Invoke();
        });

    public AddGameViewModel NewAdd() => new(Settings, Catalog, Running, Files, Ui, NewPicker);

    public LibraryViewModel NewLibrary() => new(Catalog, Settings, Content, Library, Installed, Ui, (listing, close) => NewDetail(listing, close), NewAdd);

    public GameListing Listing(string id) => GameListings.Build(Catalog.Current, Settings.Current).First(g => g.Id == id);
}

public class LibraryViewModelTests
{
    private readonly LibraryFixture _f = new();

    [Fact]
    public void Lists_every_catalog_game_alphabetically_when_none_are_installed()
    {
        var vm = _f.NewLibrary();

        vm.Games.Select(g => g.Name).ShouldBe(["Apex Legends", "Counter-Strike 2", "VALORANT"]);
        vm.HasGames.ShouldBeTrue();
        vm.IsListVisible.ShouldBeTrue();
    }

    [Fact]
    public async Task Installed_games_sort_first_and_get_the_installed_chip()
    {
        _f.Installed.Ids = ["valorant"];
        var vm = _f.NewLibrary();

        await vm.LoadInstalledCommand.ExecuteAsync(null);

        vm.Games[0].Name.ShouldBe("VALORANT");
        vm.Games[0].IsInstalled.ShouldBeTrue();
        vm.Games[0].StatusText.ShouldBe("Installed");
        vm.Games[1].StatusText.ShouldBe("Not downloaded");
    }

    [Fact]
    public async Task A_failed_installed_scan_shows_a_message_and_the_list_still_works()
    {
        _f.Installed.Throw = true;
        var vm = _f.NewLibrary();

        await vm.LoadInstalledCommand.ExecuteAsync(null);

        vm.HasError.ShouldBeTrue();
        vm.IsBusy.ShouldBeFalse();
        vm.Games.Count.ShouldBe(3);
    }

    [Fact]
    public void Search_is_case_insensitive_and_clearing_it_restores_the_list()
    {
        var vm = _f.NewLibrary();

        vm.Search = "counter";
        vm.Games.Select(g => g.Id).ShouldBe(["cs2"]);

        vm.ClearFiltersCommand.Execute(null);
        vm.Games.Count.ShouldBe(3);
    }

    [Fact]
    public void Empty_states_explain_why_the_list_is_empty()
    {
        var vm = _f.NewLibrary();

        vm.Search = "zzz";
        vm.EmptyTitle.ShouldBe("No games match");
        vm.HasGames.ShouldBeFalse();

        vm.Search = string.Empty;
        vm.Filter = LibraryFilter.Installed;
        vm.EmptyTitle.ShouldBe("No installed games found");

        _f.Settings.Update(s =>
        {
            foreach (var id in new[] { "valorant", "cs2", "apex" })
            {
                s.GetGame(id).Enabled = false;
            }
        });
        vm.Filter = LibraryFilter.Enabled;
        vm.EmptyTitle.ShouldBe("No games are turned on");
    }

    [Fact]
    public void An_empty_catalog_has_its_own_designed_empty_state()
    {
        _f.Catalog.Current = new CatalogDocument();
        var vm = _f.NewLibrary();

        vm.EmptyTitle.ShouldBe("The game list isn't available yet");
        vm.EmptyBody.ShouldContain("add any game yourself");
    }

    [Fact]
    public void Toggling_a_card_persists_the_enabled_state()
    {
        var vm = _f.NewLibrary();
        var card = vm.Games.First(g => g.Id == "cs2");
        card.IsEnabled.ShouldBeTrue();

        card.IsEnabled = false;

        _f.Settings.Current.Games["cs2"].Enabled.ShouldBeFalse();
        card.ToggleLabel.ShouldBe("Show wallpapers for Counter-Strike 2");
    }

    [Fact]
    public void Enabled_filter_follows_the_toggle_immediately()
    {
        var vm = _f.NewLibrary();
        vm.Filter = LibraryFilter.Enabled;
        vm.Games.Count.ShouldBe(3);

        vm.Games.First(g => g.Id == "apex").IsEnabled = false;

        vm.Games.Select(g => g.Id).ShouldNotContain("apex");
    }

    [Fact]
    public void Chips_reflect_download_progress_ready_and_failure()
    {
        var vm = _f.NewLibrary();
        var cs2 = vm.Games.First(g => g.Id == "cs2");

        _f.Content.RaiseProgress(new PackProgress("game.cs2", PackStateKind.Downloading, 0.42));
        cs2.StatusText.ShouldBe("Downloading 42%");

        _f.Content.RaiseProgress(new PackProgress("game.cs2", PackStateKind.Ready, 1));
        cs2.StatusText.ShouldBe("Ready");

        _f.Content.RaiseProgress(new PackProgress("game.cs2", PackStateKind.Failed, 0));
        cs2.StatusText.ShouldBe("Download failed");
    }

    [Fact]
    public void Card_thumbnail_prefers_the_hero_wallpaper()
    {
        _f.Content.Previews["cs2-min"] = "/t/min.jpg";
        _f.Content.Previews["cs2-hero"] = "/t/hero.jpg";

        var vm = _f.NewLibrary();

        vm.Games.First(g => g.Id == "cs2").ThumbnailPath.ShouldBe("/t/hero.jpg");
        vm.Games.First(g => g.Id == "valorant").ThumbnailPath.ShouldBeNull();
        vm.Games.First(g => g.Id == "valorant").Initial.ShouldBe("V");
    }

    [Fact]
    public void Custom_games_appear_with_their_own_chip()
    {
        _f.Settings.Update(s => s.CustomGames.Add(new CustomGame { Id = "custom-1", DisplayName = "My Indie", ExeNames = ["indie.exe"], Wallpapers = ["user:a.png"] }));

        var vm = _f.NewLibrary();

        var card = vm.Games.First(g => g.Id == "custom-1");
        card.IsCustom.ShouldBeTrue();
        card.StatusText.ShouldBe("Added by you");
    }

    [Fact]
    public void Opening_a_game_shows_its_detail_and_closing_returns_to_the_list()
    {
        var vm = _f.NewLibrary();

        vm.OpenGameCommand.Execute(vm.Games.First(g => g.Id == "cs2"));
        vm.Detail.ShouldNotBeNull().Name.ShouldBe("Counter-Strike 2");
        vm.IsListVisible.ShouldBeFalse();

        vm.CloseDetailCommand.Execute(null);
        vm.Detail.ShouldBeNull();
        vm.IsListVisible.ShouldBeTrue();
    }

    [Fact]
    public void Add_game_flow_adds_a_card_and_shows_a_confirmation()
    {
        var vm = _f.NewLibrary();
        vm.OpenAddGameCommand.Execute(null);
        vm.AddGame.ShouldNotBeNull();
        vm.IsListVisible.ShouldBeFalse();

        _f.Settings.Update(s => s.CustomGames.Add(new CustomGame { Id = "custom-9", DisplayName = "Fresh", ExeNames = ["fresh.exe"], Wallpapers = ["x"] }));
        vm.AddGame!.Finished!("Fresh");

        vm.AddGame.ShouldBeNull();
        vm.Toast.ShouldBe("Fresh was added.");
        vm.Games.ShouldContain(g => g.Name == "Fresh");
    }

    [Fact]
    public void Cancelling_add_game_just_closes_it()
    {
        var vm = _f.NewLibrary();
        vm.OpenAddGameCommand.Execute(null);

        vm.AddGame!.CancelCommand.Execute(null);

        vm.AddGame.ShouldBeNull();
        vm.Toast.ShouldBeNull();
    }

    [Fact]
    public void Disposing_releases_the_open_detail_and_event_handlers()
    {
        var vm = _f.NewLibrary();
        vm.OpenGameCommand.Execute(vm.Games[0]);
        vm.Dispose();

        _f.Content.RaiseProgress(new PackProgress("game.cs2", PackStateKind.Downloading, 0.5));

        vm.Games.First(g => g.Id == "cs2").StatusText.ShouldNotBe("Downloading 50%");
    }
}

public class GameDetailViewModelTests
{
    private readonly LibraryFixture _f = new();

    public GameDetailViewModelTests()
    {
        foreach (var id in new[] { "cs2-hero", "cs2-min", "cs2-mood" })
        {
            _f.Library.Available.Add(id);
            _f.Content.Previews[id] = "/t/" + id + ".jpg";
        }
    }

    private GameDetailViewModel Detail() => _f.NewDetail(_f.Listing("cs2"));

    [Fact]
    public void Shows_the_packs_wallpapers_with_titles_previews_and_availability()
    {
        _f.Library.Available.Remove("cs2-mood");

        var vm = Detail();

        vm.Wallpapers.Select(w => w.Title).ShouldBe(["Title cs2-hero", "Title cs2-min", "Title cs2-mood"]);
        vm.Wallpapers[0].PreviewPath.ShouldBe("/t/cs2-hero.jpg");
        vm.Wallpapers[2].IsAvailable.ShouldBeFalse();
        vm.Wallpapers.ShouldAllBe(w => w.IsIncluded);
        vm.ShowRotationOptions.ShouldBeTrue();
    }

    [Fact]
    public void Excluded_wallpapers_show_unchecked_and_unchecking_persists()
    {
        _f.Settings.Update(s => s.GetGame("cs2").Excluded = ["cs2-mood"]);
        var vm = Detail();
        vm.Wallpapers[2].IsIncluded.ShouldBeFalse();

        vm.Wallpapers[1].IsIncluded = false;

        _f.Settings.Current.Games["cs2"].Excluded.ShouldBe(["cs2-min", "cs2-mood"], ignoreOrder: true);
    }

    [Fact]
    public void The_last_included_wallpaper_cannot_be_unchecked()
    {
        var vm = Detail();
        vm.Wallpapers[1].IsIncluded = false;
        vm.Wallpapers[2].IsIncluded = false;

        vm.Wallpapers[0].IsIncluded = false;

        vm.Wallpapers[0].IsIncluded.ShouldBeTrue();
        vm.Notice.ShouldBe("Keep at least one wallpaper turned on.");
        _f.Settings.Current.Games["cs2"].Excluded.ShouldNotContain("cs2-hero");
    }

    [Fact]
    public void Favouriting_switches_to_fixed_mode_and_unfavouriting_goes_back_to_rotating()
    {
        var vm = Detail();

        vm.Wallpapers[1].IsFavorite = true;

        _f.Settings.Current.Games["cs2"].Mode.ShouldBe(WallpaperMode.Fixed);
        _f.Settings.Current.Games["cs2"].FixedWallpaperId.ShouldBe("cs2-min");
        vm.IsRotate.ShouldBeFalse();
        vm.Wallpapers[1].FavoriteLabel.ShouldBe("Stop using Title cs2-min as my favourite");

        vm.Wallpapers[1].IsFavorite = false;

        _f.Settings.Current.Games["cs2"].Mode.ShouldBe(WallpaperMode.Rotate);
        _f.Settings.Current.Games["cs2"].FixedWallpaperId.ShouldBeNull();
        vm.IsRotate.ShouldBeTrue();
    }

    [Fact]
    public void Only_one_favourite_at_a_time()
    {
        var vm = Detail();
        vm.Wallpapers[0].IsFavorite = true;

        vm.Wallpapers[2].IsFavorite = true;

        vm.Wallpapers[0].IsFavorite.ShouldBeFalse();
        _f.Settings.Current.Games["cs2"].FixedWallpaperId.ShouldBe("cs2-mood");
    }

    [Fact]
    public void Switching_to_fixed_without_a_favourite_picks_the_first_available_wallpaper()
    {
        var vm = Detail();

        vm.IsRotate = false;

        _f.Settings.Current.Games["cs2"].FixedWallpaperId.ShouldBe("cs2-hero");
    }

    [Fact]
    public void Interval_and_order_persist_and_default_to_per_session_shuffle()
    {
        var vm = Detail();
        vm.SelectedInterval!.Value.Kind.ShouldBe(RotationIntervalKind.Session);
        vm.IsShuffle.ShouldBeTrue();

        vm.SelectedInterval = vm.IntervalChoices.First(c => c.Value == RotationInterval.Every(TimeSpan.FromMinutes(30)));
        vm.IsShuffle = false;

        _f.Settings.Current.Games["cs2"].Interval.ShouldBe(RotationInterval.Every(TimeSpan.FromMinutes(30)));
        _f.Settings.Current.Games["cs2"].Order.ShouldBe(RotationOrder.Sequential);
        vm.SelectedInterval.Label.ShouldBe("Every 30 minutes");
    }

    [Fact]
    public void Preview_only_works_for_wallpapers_that_are_on_disk()
    {
        _f.Library.Available.Remove("cs2-mood");
        var vm = Detail();

        vm.PreviewCommand.Execute(vm.Wallpapers[0]);
        vm.PreviewCommand.Execute(vm.Wallpapers[2]);

        _f.Controller.Calls.ShouldBe(["preview:cs2-hero"]);
    }

    [Fact]
    public void Download_requests_the_pack_and_status_follows_progress()
    {
        var vm = Detail();
        vm.PackStatusText.ShouldBe("Not downloaded");
        vm.CanDownload.ShouldBeTrue();

        vm.DownloadCommand.Execute(null);
        _f.Content.Requested.ShouldBe(["game.cs2"]);

        _f.Content.RaiseProgress(new PackProgress("game.cs2", PackStateKind.Downloading, 0.5));
        vm.PackStatusText.ShouldBe("Downloading… 50%");
        vm.CanDownload.ShouldBeFalse();

        _f.Content.RaiseProgress(new PackProgress("game.cs2", PackStateKind.Failed, 0));
        vm.PackStatusText.ShouldContain("didn't finish");
        vm.CanDownload.ShouldBeTrue();
    }

    [Fact]
    public void Detection_summary_lists_exes_and_steam_ids_read_only()
    {
        var vm = Detail();

        vm.DetectionLines.ShouldBe(["Program: cs2.exe", "Steam app: 730"]);
        vm.IsCustom.ShouldBeFalse();
    }

    [Fact]
    public void Enabled_toggle_persists()
    {
        var vm = Detail();

        vm.Enabled = false;

        _f.Settings.Current.Games["cs2"].Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Background_download_choice_can_be_changed_after_onboarding()
    {
        using var vm = Detail();
        vm.PrefetchWallpapers = false;
        _f.Settings.Current.Games["cs2"].PrefetchWallpapers.ShouldBeFalse();
        _f.Settings.Current.IsGameEnabled("cs2").ShouldBeTrue();
        _f.Settings.ChangeExternally(s => s.GetGame("cs2").PrefetchWallpapers = true);
        vm.PrefetchWallpapers.ShouldBeTrue();
    }

    private GameDetailViewModel CustomDetail(out string id)
    {
        _f.Settings.Update(s => s.CustomGames.Add(new CustomGame { Id = "custom-1", DisplayName = "My Indie", ExeNames = ["indie.exe"], Wallpapers = ["cs2-hero", "user:a.png"] }));
        _f.Content.Images.Add(new UserImage("user:a.png", "/u/a.png", 2000, 1000));
        _f.Library.Available.Add("user:a.png");
        id = "custom-1";
        return _f.NewDetail(_f.Listing("custom-1"));
    }

    [Fact]
    public void Custom_game_shows_only_its_chosen_wallpapers_and_an_editable_exe_list()
    {
        var vm = CustomDetail(out _);

        vm.IsCustom.ShouldBeTrue();
        vm.Wallpapers.Select(w => w.Id).ShouldBe(["cs2-hero", "user:a.png"]);
        vm.Wallpapers[1].Title.ShouldBe("a.png");
        vm.ExeNamesText.ShouldBe("indie.exe");
    }

    [Theory]
    [InlineData("game.exe\nother.exe", true)]
    [InlineData("game.exe, other.exe", true)]
    [InlineData("game", false)]
    [InlineData(@"C:\Games\game.exe", false)]
    [InlineData("a/b.exe", false)]
    [InlineData(".exe", false)]
    [InlineData("", false)]
    public void Custom_exe_names_are_validated_before_they_are_saved(string text, bool valid)
    {
        var vm = CustomDetail(out var id);

        vm.ExeNamesText = text;

        (vm.ExeError is null).ShouldBe(valid);
        if (!valid)
        {
            _f.Settings.Current.CustomGames.First(c => c.Id == id).ExeNames.ShouldBe(["indie.exe"]);
        }
        else
        {
            _f.Settings.Current.CustomGames.First(c => c.Id == id).ExeNames.Count.ShouldBeGreaterThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task Removing_a_custom_game_confirms_deletes_it_and_leaves_the_page()
    {
        var vm = CustomDetail(out _);

        await vm.RemoveCommand.ExecuteAsync(null);

        _f.Settings.Current.CustomGames.ShouldBeEmpty();
        _f.RemovedCallbacks.ShouldBe(1);
    }

    [Fact]
    public async Task Declining_the_removal_keeps_the_game()
    {
        var vm = CustomDetail(out _);
        _f.Dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(false);

        await vm.RemoveCommand.ExecuteAsync(null);

        _f.Settings.Current.CustomGames.ShouldHaveSingleItem();
        _f.RemovedCallbacks.ShouldBe(0);
    }

    [Fact]
    public async Task Catalog_games_cannot_be_removed()
    {
        var vm = Detail();

        await vm.RemoveCommand.ExecuteAsync(null);

        _f.RemovedCallbacks.ShouldBe(0);
    }

    [Fact]
    public void Suggest_opens_a_prefilled_issue_with_only_the_program_and_display_name()
    {
        var vm = CustomDetail(out _);

        vm.SuggestCommand.Execute(null);

        var url = (string)_f.Launcher.ReceivedCalls().Single().GetArguments()[0]!;
        url.ShouldStartWith(AppLinks.Repository + "/issues/new?");
        Uri.UnescapeDataString(url).ShouldContain("Program: indie.exe");
        Uri.UnescapeDataString(url).ShouldContain("Display name: My Indie");
        Uri.UnescapeDataString(url).ShouldNotContain("\\");
    }

    [Fact]
    public void Picker_adds_wallpapers_to_a_custom_game()
    {
        var vm = CustomDetail(out var id);
        _f.Library.Available.Add("cs2-min");

        vm.OpenPickerCommand.Execute(null);
        vm.Picker.ShouldNotBeNull();
        vm.Picker!.Content.Items.Single(i => i.Id == "cs2-min").IsSelected = true;
        vm.ConfirmPickerCommand.Execute(null);

        vm.Picker.ShouldBeNull();
        _f.Settings.Current.CustomGames.First(c => c.Id == id).Wallpapers.ShouldBe(["cs2-hero", "user:a.png", "cs2-min"], ignoreOrder: true);
    }

    [Fact]
    public void Cancelling_the_picker_changes_nothing()
    {
        var vm = CustomDetail(out var id);
        vm.OpenPickerCommand.Execute(null);

        vm.CancelPickerCommand.Execute(null);

        vm.Picker.ShouldBeNull();
        _f.Settings.Current.CustomGames.First(c => c.Id == id).Wallpapers.Count.ShouldBe(2);
    }

    [Fact]
    public void Detail_reloads_when_the_pack_finishes_downloading()
    {
        _f.Library.Available.Remove("cs2-mood");
        var vm = Detail();
        vm.Wallpapers[2].IsAvailable.ShouldBeFalse();

        _f.Library.Available.Add("cs2-mood");
        _f.Content.RaisePackChanged("game.cs2");

        vm.Wallpapers[2].IsAvailable.ShouldBeTrue();
    }
}

public class AddGameViewModelTests
{
    private readonly LibraryFixture _f = new();

    public AddGameViewModelTests()
    {
        _f.Library.Available.Add("cs2-hero");
        _f.Content.Previews["cs2-hero"] = "/t.jpg";
        _f.Running.Apps = [new RunningApp("zeta.exe", "Zeta Game"), new RunningApp("alpha.exe", "Alpha Game"), new RunningApp("steam.exe", "Steam")];
    }

    private AddGameViewModel Vm() => _f.NewAdd();

    private static void ChooseWallpaper(AddGameViewModel vm)
    {
        vm.OpenPickerCommand.Execute(null);
        vm.Picker!.Content.Items.First().IsSelected = true;
        vm.ConfirmPickerCommand.Execute(null);
    }

    [Fact]
    public async Task Running_apps_are_listed_sorted_by_title_and_searchable()
    {
        var vm = Vm();

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.RunningApps.Select(a => a.Title).ShouldBe(["Alpha Game", "Steam", "Zeta Game"]);

        vm.Search = "zeta";
        vm.RunningApps.ShouldHaveSingleItem().ExeName.ShouldBe("zeta.exe");
        vm.HasRunningApps.ShouldBeTrue();
    }

    [Fact]
    public async Task No_windowed_apps_gives_an_empty_state()
    {
        _f.Running.Apps = [];
        var vm = Vm();

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.HasRunningApps.ShouldBeFalse();
    }

    [Fact]
    public async Task Picking_a_running_app_prefills_exe_and_name()
    {
        var vm = Vm();
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.SelectedApp = vm.RunningApps.First(a => a.ExeName == "alpha.exe");

        vm.ExeName.ShouldBe("alpha.exe");
        vm.DisplayName.ShouldBe("Alpha Game");
        vm.ValidationMessage.ShouldBeNull();
    }

    [Fact]
    public void Browsing_to_an_exe_prefills_from_the_file_name()
    {
        _f.Files.Executable = @"D:\Games\Cool Game\CoolGame.exe";
        var vm = Vm();

        vm.BrowseExeCommand.Execute(null);

        vm.ExeName.ShouldBe("CoolGame.exe");
    }

    [Fact]
    public void Cancelled_browse_changes_nothing()
    {
        var vm = Vm();

        vm.BrowseExeCommand.Execute(null);

        vm.ExeName.ShouldBeNull();
    }

    [Fact]
    public async Task Launchers_are_refused_with_an_explanation()
    {
        var vm = Vm();
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.SelectedApp = vm.RunningApps.First(a => a.ExeName == "steam.exe");

        vm.ValidationMessage.ShouldBe("steam.exe is a launcher or helper, not a game, so PrettyDesk can't use it.");
    }

    [Fact]
    public void Exes_already_covered_by_a_catalog_game_are_refused()
    {
        _f.Files.Executable = @"C:\x\cs2.exe";
        var vm = Vm();

        vm.BrowseExeCommand.Execute(null);

        vm.ValidationMessage.ShouldBe("cs2.exe is already set up as Counter-Strike 2.");
    }

    [Fact]
    public void Save_needs_a_program_a_name_and_at_least_one_wallpaper()
    {
        var vm = Vm();
        vm.CanSave.ShouldBeFalse();

        _f.Files.Executable = @"C:\x\Indie.exe";
        vm.BrowseExeCommand.Execute(null);
        vm.CanSave.ShouldBeFalse();

        ChooseWallpaper(vm);
        vm.CanSave.ShouldBeTrue();
        vm.SaveCommand.CanExecute(null).ShouldBeTrue();

        vm.DisplayName = "  ";
        vm.CanSave.ShouldBeFalse();
    }

    [Fact]
    public void Saving_adds_a_custom_game_with_its_exe_and_wallpapers_and_reports_the_name()
    {
        _f.Files.Executable = @"C:\x\Indie.exe";
        var vm = Vm();
        string? finished = null;
        vm.Finished = name => finished = name;
        vm.BrowseExeCommand.Execute(null);
        vm.DisplayName = "My Indie Game";
        ChooseWallpaper(vm);

        vm.SaveCommand.Execute(null);

        var custom = _f.Settings.Current.CustomGames.ShouldHaveSingleItem();
        custom.DisplayName.ShouldBe("My Indie Game");
        custom.ExeNames.ShouldBe(["Indie.exe"]);
        custom.Wallpapers.ShouldBe(["cs2-hero"]);
        custom.Id.ShouldStartWith("custom-");
        finished.ShouldBe("My Indie Game");
    }

    [Fact]
    public void A_settings_failure_is_a_friendly_message_and_does_not_close_the_dialog()
    {
        var settings = Substitute.For<ISettingsProvider>();
        settings.Current.Returns(new AppSettings());
        settings.When(s => s.Update(Arg.Any<Action<AppSettings>>())).Do(_ => throw new IOException());
        _f.Files.Executable = @"C:\x\Indie.exe";
        var vm = new AddGameViewModel(settings, _f.Catalog, _f.Running, _f.Files, _f.Ui, _f.NewPicker);
        var closed = false;
        vm.Finished = _ => closed = true;
        vm.BrowseExeCommand.Execute(null);
        ChooseWallpaper(vm);

        vm.SaveCommand.Execute(null);

        vm.ValidationMessage.ShouldBe("Couldn't add the game. Try again.");
        closed.ShouldBeFalse();
    }

    [Fact]
    public void Picker_only_offers_wallpapers_that_exist_on_disk_and_supports_search_and_user_images()
    {
        _f.Library.Available.Add("cs2-min");
        _f.Content.Images.Add(new UserImage("user:mine.png", "/u/mine.png", 2000, 1000));
        var picker = _f.NewPicker([]);

        picker.Items.Select(i => i.Id).ShouldBe(["user:mine.png", "cs2-hero", "cs2-min"]);
        picker.Items.ShouldNotContain(i => i.Id == "cs2-mood");

        picker.Search = "mine";
        picker.Items.ShouldHaveSingleItem();
        picker.IsEmpty.ShouldBeFalse();
        picker.Search = "nothing-like-this";
        picker.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Picker_import_adds_and_selects_new_images_and_reports_skipped_ones()
    {
        _f.Files.Images = ["/p/ok.png", "/p/tiny.png"];
        _f.Content.NextImport = null;
        var picker = _f.NewPicker([]);

        picker.ImportImagesCommand.Execute(null);

        picker.SelectedIds.ShouldContain("user:ok.png");
        picker.SelectedText.ShouldBe("2 selected");
    }

    [Fact]
    public void Picker_import_reports_images_that_are_too_small()
    {
        _f.Files.Images = ["/p/tiny.png"];
        _f.Content.NextImport = new ImportResult(null, ImportFailure.TooSmall, "That image is 100×100.");
        var picker = _f.NewPicker([]);

        picker.ImportImagesCommand.Execute(null);

        picker.Notice.ShouldBe("1 image(s) couldn't be added: That image is 100×100.");
        picker.SelectedCount.ShouldBe(0);
    }

    [Fact]
    public void Picker_preselects_what_is_already_chosen()
    {
        var picker = _f.NewPicker(["cs2-hero"]);

        picker.SelectedIds.ShouldBe(["cs2-hero"]);
    }

    [Fact]
    public void Picker_empty_state_is_exposed()
    {
        _f.Library.Available.Clear();

        _f.NewPicker([]).IsEmpty.ShouldBeTrue();
    }
}
