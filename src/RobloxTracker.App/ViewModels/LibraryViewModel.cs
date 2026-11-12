using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxTracker.App.Services;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.ViewModels;

public partial class LibraryViewModel(StatsService stats, LibraryService library, NavigationService nav) : PageViewModel
{
    private const int All = -1;
    private const int NoCategory = 0;

    private List<GameCardItem> _all = [];
    private bool _refreshing;

    public IReadOnlyList<string> SortOptions { get; } =
        ["Temps de jeu", "Joué récemment", "Cette semaine", "Nom", "Ajouté récemment"];

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private bool favoritesOnly;
    [ObservableProperty] private string selectedSort = "Temps de jeu";
    [ObservableProperty] private IReadOnlyList<FilterOption> categories = [];
    [ObservableProperty] private FilterOption? selectedCategory;
    [ObservableProperty] private IReadOnlyList<FilterOption> groups = [];
    [ObservableProperty] private FilterOption? selectedGroup;
    [ObservableProperty] private IReadOnlyList<GameCardItem> games = [];
    [ObservableProperty] private string countText = "";

    /// <summary>Nombre de cartes par ligne, fixé par la vue selon sa largeur (les lignes sont virtualisées).</summary>
    [ObservableProperty] private int columns = 4;
    [ObservableProperty] private IReadOnlyList<GameCardItem[]> rows = [];

    partial void OnColumnsChanged(int value) => BuildRows();
    partial void OnGamesChanged(IReadOnlyList<GameCardItem> value) => BuildRows();

    private void BuildRows() => Rows = Games.Chunk(Math.Max(1, Columns)).ToList();

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnFavoritesOnlyChanged(bool value) => ApplyFilter();
    partial void OnSelectedSortChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(FilterOption? value) => ApplyFilter();
    partial void OnSelectedGroupChanged(FilterOption? value) => ApplyFilter();

    public override void Refresh()
    {
        _refreshing = true;
        _all = stats.GetGameStats().Select(s => new GameCardItem(s, library)).ToList();

        var previousCategory = SelectedCategory?.Id ?? All;
        Categories = [new(All, "Toutes les catégories"), new(NoCategory, "Sans catégorie"),
            .. library.GetCategories().Select(c => new FilterOption(c.Id, c.Name))];
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == previousCategory) ?? Categories[0];

        var previousGroup = SelectedGroup?.Id ?? All;
        Groups = [new(All, "Tous les groupes"), .. library.GetGroups().Select(g => new FilterOption(g.Id, g.Name))];
        SelectedGroup = Groups.FirstOrDefault(g => g.Id == previousGroup) ?? Groups[0];

        _refreshing = false;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_refreshing) return;

        IEnumerable<GameCardItem> query = _all;
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(g => g.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)
                                     || (g.CreatorName?.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase) ?? false));
        if (FavoritesOnly) query = query.Where(g => g.IsFavorite);

        query = SelectedCategory?.Id switch
        {
            null or All => query,
            NoCategory => query.Where(g => g.CategoryId is null),
            var id => query.Where(g => g.CategoryId == id),
        };
        if (SelectedGroup is { Id: > 0 } group) query = query.Where(g => g.GroupIds.Contains(group.Id));

        query = SelectedSort switch
        {
            "Joué récemment" => query.OrderByDescending(g => g.LastPlayedUtc),
            "Cette semaine" => query.OrderByDescending(g => g.Last7Days),
            "Nom" => query.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            "Ajouté récemment" => query.OrderByDescending(g => g.FirstSeenUtc),
            _ => query.OrderByDescending(g => g.Total),
        };

        Games = query.ToList();
        CountText = Games.Count == _all.Count ? $"{_all.Count} jeux" : $"{Games.Count} sur {_all.Count} jeux";
    }

    [RelayCommand]
    private void OpenGame(long universeId) => nav.OpenGame(universeId);

    [RelayCommand]
    private void ClearFilters()
    {
        _refreshing = true;
        SearchText = "";
        FavoritesOnly = false;
        SelectedCategory = Categories.FirstOrDefault();
        SelectedGroup = Groups.FirstOrDefault();
        _refreshing = false;
        ApplyFilter();
    }
}
