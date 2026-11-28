using CommunityToolkit.Mvvm.ComponentModel;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    /// <summary>Recharge les données depuis la base (appelé à l'affichage et quand le tracker détecte un changement).</summary>
    public virtual void Refresh() { }
}

/// <summary>Option de filtre/sélection affichée dans une ComboBox. Id négatif = « Tous ».</summary>
public sealed record FilterOption(int Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record NavEntry(string Title, string Glyph, PageViewModel Page);

/// <summary>Barre d'un graphe. <see cref="Date"/> : jour (ou mois) représenté, quand la barre est cliquable.</summary>
public sealed record BarItem(string Label, string Tooltip, double BarHeight, bool IsToday, string ValueText = "", DateTime? Date = null);

public sealed record TopGameItem(GameCardItem Card, double Ratio, int Rank = 0);

public sealed record DayHeader(string Label, TimeSpan Total);

/// <summary>Un jeu tel qu'affiché dans les listes, avec ses statistiques.</summary>
public partial class GameCardItem : ObservableObject
{
    private readonly LibraryService _library;

    public GameCardItem(GameStats stats, LibraryService library)
    {
        _library = library;
        var g = stats.Game;
        UniverseId = g.UniverseId;
        Name = g.Name;
        IconPath = g.IconPath;
        ThumbnailPath = g.ThumbnailPath;
        CreatorName = g.CreatorName;
        Genre = g.Genre;
        CategoryId = g.CategoryId;
        CategoryName = g.Category?.Name;
        CategoryColor = g.Category?.ColorHex;
        GroupIds = g.Groups.Select(x => x.Id).ToHashSet();
        GroupNames = string.Join(", ", g.Groups.Select(x => x.Name).OrderBy(x => x));
        Total = stats.Total;
        Last7Days = stats.Last7Days;
        SessionCount = stats.SessionCount;
        LastPlayedUtc = stats.LastPlayedUtc;
        FirstSeenUtc = g.FirstSeenUtc;
        isFavorite = g.IsFavorite;
    }

    public long UniverseId { get; }
    public string Name { get; }
    public string? IconPath { get; }
    public string? ThumbnailPath { get; }
    public string? CreatorName { get; }
    public string? Genre { get; }
    public int? CategoryId { get; }
    public string? CategoryName { get; }
    public string? CategoryColor { get; }
    public HashSet<int> GroupIds { get; }
    public string GroupNames { get; }
    public TimeSpan Total { get; }
    public TimeSpan Last7Days { get; }
    public int SessionCount { get; }
    public DateTime? LastPlayedUtc { get; }
    public DateTime FirstSeenUtc { get; }

    [ObservableProperty]
    private bool isFavorite;

    partial void OnIsFavoriteChanged(bool value) => _library.SetFavorite(UniverseId, value);
}
