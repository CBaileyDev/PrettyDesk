using CommunityToolkit.Mvvm.ComponentModel;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Settings;
using PrettyDesk.Presentation.Resources;

namespace PrettyDesk.Presentation.ViewModels;

/// <summary>Windows path pieces that behave the same on every host OS (the paths we handle are always Windows paths).</summary>
public static class WindowsPaths
{
    public static string FileName(string path)
    {
        var index = path.LastIndexOfAny(['\\', '/']);
        return index < 0 ? path : path[(index + 1)..];
    }

    public static string FileNameWithoutExtension(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }
}

public static class AppLinks
{
    // OWNER-DECISION: repository location for the issue tracker link. Defaults to this repository.
    public const string Repository = "https://github.com/CBaileyDev/PrettyDesk";
    public const string Issues = Repository + "/issues";
    public const string Privacy = Repository + "#privacy";

    /// <summary>A prefilled "suggest a game" issue containing only the exe name and display name (FR-CUSTOM-3).</summary>
    public static string SuggestGame(string displayName, string exeName) =>
        Repository + "/issues/new?title=" + Uri.EscapeDataString("Game suggestion: " + displayName)
        + "&body=" + Uri.EscapeDataString($"Display name: {displayName}\nProgram: {exeName}\n");
}

/// <summary>A game in the Library: catalog entry or user-added (FR-CUSTOM-2).</summary>
public sealed record GameListing(string Id, string DisplayName, string? PackId, bool IsCustom, DetectionRules Rules);

public static class GameListings
{
    public static IReadOnlyList<GameListing> Build(CatalogDocument catalog, AppSettings settings)
    {
        var listings = catalog.Games.Select(g => new GameListing(g.Id, g.DisplayName, g.PackId, false, g.Detection)).ToList();
        listings.AddRange(settings.CustomGames.Select(c => new GameListing(c.Id, c.DisplayName, null, true, new DetectionRules { ExeNames = c.ExeNames.ToList() })));
        return listings;
    }
}

public static class WallpaperTitles
{
    public static string Resolve(CatalogDocument catalog, string wallpaperId)
    {
        if (catalog.FindWallpaper(wallpaperId) is { } entry && !string.IsNullOrWhiteSpace(entry.Title))
        {
            return entry.Title;
        }

        return wallpaperId.StartsWith("user:", StringComparison.Ordinal) ? wallpaperId["user:".Length..] : wallpaperId;
    }
}

/// <summary>A label for a rotation interval, built from resources so it can be localised.</summary>
public sealed record IntervalChoice(RotationInterval Value, string Label);

public static class IntervalLabels
{
    public static string Of(RotationInterval interval)
    {
        switch (interval.Kind)
        {
            case RotationIntervalKind.Unlock:
                return Strings.Interval_Unlock;
            case RotationIntervalKind.Session:
                return Strings.Interval_Session;
            case RotationIntervalKind.Never:
                return Strings.Interval_Never;
        }

        var d = interval.Duration;
        if (d.TotalDays >= 1 && d.TotalDays % 1 == 0)
        {
            return d.TotalDays == 1 ? Strings.Interval_Daily : Strings.Format(Strings.Interval_Days, (int)d.TotalDays);
        }

        if (d.TotalHours >= 1 && d.TotalHours % 1 == 0)
        {
            return d.TotalHours == 1 ? Strings.Interval_Hour : Strings.Format(Strings.Interval_Hours, (int)d.TotalHours);
        }

        return Strings.Format(Strings.Interval_Minutes, (int)d.TotalMinutes);
    }

    public static IReadOnlyList<IntervalChoice> Presets(bool includeSession) =>
        (includeSession ? new[] { RotationInterval.Session } : [])
            .Concat(RotationInterval.Presets)
            .Select(i => new IntervalChoice(i, Of(i)))
            .ToList();
}

/// <summary>One wallpaper in a strip: include checkbox, favourite star and preview button (SPEC §7.2).</summary>
public sealed partial class WallpaperItemViewModel : ObservableObject
{
    private bool _quiet;

    [ObservableProperty]
    private string? _previewPath;

    [ObservableProperty]
    private bool _isIncluded = true;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isAvailable;

    public WallpaperItemViewModel(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    public string Title { get; }

    public string IncludeLabel => Strings.Format(Strings.Game_Include, Title);

    public string FavoriteLabel => Strings.Format(IsFavorite ? Strings.Game_Unfavorite : Strings.Game_Favorite, Title);

    public Action<WallpaperItemViewModel>? IncludedChanged { get; set; }

    public Action<WallpaperItemViewModel>? FavoriteChanged { get; set; }

    /// <summary>Sets state from the model without notifying the owner (used when reloading).</summary>
    public void SetQuietly(bool included, bool favorite)
    {
        _quiet = true;
        try
        {
            IsIncluded = included;
            IsFavorite = favorite;
        }
        finally
        {
            _quiet = false;
        }
    }

    partial void OnIsIncludedChanged(bool value)
    {
        if (!_quiet)
        {
            IncludedChanged?.Invoke(this);
        }
    }

    partial void OnIsFavoriteChanged(bool value)
    {
        OnPropertyChanged(nameof(FavoriteLabel));
        if (!_quiet)
        {
            FavoriteChanged?.Invoke(this);
        }
    }
}
