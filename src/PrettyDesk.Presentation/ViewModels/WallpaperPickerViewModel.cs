using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Orchestration;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.Services;

namespace PrettyDesk.Presentation.ViewModels;

public sealed partial class PickerItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public PickerItemViewModel(string id, string title, string group, string? previewPath)
    {
        Id = id;
        Title = title;
        Group = group;
        PreviewPath = previewPath;
    }

    public string Id { get; }

    public string Title { get; }

    public string Group { get; }

    public string? PreviewPath { get; }
}

/// <summary>
/// Chooses wallpapers from the user's own images and every wallpaper that is on disk, for "Add a game" and custom games
/// (FR-CUSTOM-1). Only wallpapers that exist locally are offered, so a choice is always renderable.
/// </summary>
public sealed partial class WallpaperPickerViewModel : ViewModelBase
{
    private readonly List<PickerItemViewModel> _all = [];
    private readonly IContentBrowser _content;
    private readonly IFilePicker _files;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private string? _notice;

    public WallpaperPickerViewModel(CatalogDocument catalog, IContentLibrary library, IContentBrowser content, IFilePicker files, IEnumerable<string>? alreadySelected = null)
    {
        _content = content;
        _files = files;
        var selected = alreadySelected?.ToHashSet(StringComparer.Ordinal) ?? [];
        LoadItems(catalog, library, selected);
        ApplyFilter();
    }

    public ObservableCollection<PickerItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public int SelectedCount => _all.Count(i => i.IsSelected);

    public string SelectedText => Strings.Format(Strings.Picker_Selected, SelectedCount);

    public IReadOnlyList<string> SelectedIds => _all.Where(i => i.IsSelected).Select(i => i.Id).ToList();

    partial void OnSearchChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void ImportImages()
    {
        var skipped = new List<string>();
        foreach (var path in _files.PickImages())
        {
            var result = _content.ImportUserImage(path);
            if (result.Ok)
            {
                var image = result.Image!;
                if (_all.All(i => i.Id != image.Id))
                {
                    _all.Insert(0, new PickerItemViewModel(image.Id, image.Id["user:".Length..], Strings.Picker_GroupMine, image.Path) { IsSelected = true });
                }
                else
                {
                    _all.First(i => i.Id == image.Id).IsSelected = true;
                }
            }
            else if (result.Message is { } message)
            {
                skipped.Add(message);
            }
        }

        Notice = skipped.Count == 0 ? null : Strings.Format(Strings.Add_ImportSkipped, skipped.Count, skipped[0]);
        ApplyFilter();
    }

    private void LoadItems(CatalogDocument catalog, IContentLibrary library, HashSet<string> selected)
    {
        foreach (var image in _content.ListUserImages())
        {
            _all.Add(new PickerItemViewModel(image.Id, image.Id["user:".Length..], Strings.Picker_GroupMine, image.Path) { IsSelected = selected.Contains(image.Id) });
        }

        foreach (var pack in catalog.Packs)
        {
            foreach (var wallpaper in pack.Wallpapers)
            {
                if (library.TryGetAsset(wallpaper.Id) is null)
                {
                    continue;
                }

                _all.Add(new PickerItemViewModel(wallpaper.Id, wallpaper.Title, pack.Title, _content.GetPreviewImagePath(wallpaper.Id)) { IsSelected = selected.Contains(wallpaper.Id) });
            }
        }

        foreach (var item in _all)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PickerItemViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(SelectedText));
                }
            };
        }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        var term = Search.Trim();
        foreach (var item in _all.Where(i => term.Length == 0 || i.Title.Contains(term, StringComparison.OrdinalIgnoreCase) || i.Group.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedText));
    }
}
