using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxTracker.App.Services;
using RobloxTracker.Core.Models;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.ViewModels;

public partial class GroupToggle(int id, string name, bool initiallyMember, Action<int, bool> onChanged) : ObservableObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;

    [ObservableProperty] private bool isMember = initiallyMember;

    partial void OnIsMemberChanged(bool value) => onChanged(Id, value);
}

public partial class GameDetailViewModel(
    long universeId,
    LibraryService library,
    StatsService stats,
    GameCatalogService catalog,
    NavigationService nav) : PageViewModel
{
    private const int NoCategory = 0;
    private bool _loading;
    private bool _notesLoaded;

    public long UniverseId { get; } = universeId;

    public PeriodStatsViewModel Period { get; } = new(stats, universeId);

    [ObservableProperty] private Game? game;
    [ObservableProperty] private GameStats? gameStats;
    [ObservableProperty] private TimeSpan averageSession;
    [ObservableProperty] private bool isFavorite;
    [ObservableProperty] private IReadOnlyList<FilterOption> categoryOptions = [];
    [ObservableProperty] private FilterOption? selectedCategory;
    [ObservableProperty] private IReadOnlyList<GroupToggle> groupOptions = [];
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private IReadOnlyList<SessionInfo> sessions = [];
    [ObservableProperty] private IReadOnlyList<GameUpdate> updates = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshFromRobloxCommand))]
    private bool isRefreshing;
    [ObservableProperty] private string? likeText;

    partial void OnIsFavoriteChanged(bool value)
    {
        if (!_loading) library.SetFavorite(UniverseId, value);
    }

    partial void OnSelectedCategoryChanged(FilterOption? value)
    {
        if (!_loading && value is not null)
            library.SetCategory(UniverseId, value.Id == NoCategory ? null : value.Id);
    }

    public override void Refresh()
    {
        var g = library.GetGame(UniverseId);
        if (g is null)
        {
            nav.GoBack();
            return;
        }

        _loading = true;
        Game = g;
        GameStats = stats.GetGameStats().FirstOrDefault(s => s.Game.UniverseId == UniverseId);
        IsFavorite = g.IsFavorite;
        LikeText = g.LikeRatio is { } r ? $"{r:P0} de votes positifs" : null;

        CategoryOptions = [new(NoCategory, "Aucune catégorie"),
            .. library.GetCategories().Select(c => new FilterOption(c.Id, c.Name))];
        SelectedCategory = CategoryOptions.FirstOrDefault(c => c.Id == (g.CategoryId ?? NoCategory));

        var memberIds = g.Groups.Select(x => x.Id).ToHashSet();
        GroupOptions = library.GetGroups()
            .Select(x => new GroupToggle(x.Id, x.Name, memberIds.Contains(x.Id),
                (groupId, member) => library.SetGroupMembership(UniverseId, groupId, member)))
            .ToList();

        // Ne pas écraser une note en cours de saisie lors d'un rafraîchissement automatique.
        if (!_notesLoaded)
        {
            Notes = g.Notes ?? "";
            _notesLoaded = true;
        }

        Sessions = library.GetSessions(universeId: UniverseId);
        AverageSession = Sessions.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Sessions.Average(s => s.Duration.TotalSeconds));
        Updates = library.GetUpdatesFor(UniverseId);
        Period.Refresh();
        _loading = false;
    }

    [RelayCommand]
    private void Back() => nav.GoBack();

    [RelayCommand]
    private void Play()
    {
        if (Game is not null) Shell.LaunchRobloxPlace(Game.RootPlaceId);
    }

    [RelayCommand]
    private void OpenWeb()
    {
        if (Game is not null) Shell.Open(Game.WebUrl);
    }

    [RelayCommand]
    private void SaveNotes() => library.SetNotes(UniverseId, Notes);

    [RelayCommand(CanExecute = nameof(CanRefreshFromRoblox))]
    private async Task RefreshFromRobloxAsync()
    {
        IsRefreshing = true;
        try
        {
            await catalog.RefreshAsync([UniverseId], forceImages: true);
            if (catalog.LastError is { } error)
                MessageBox.Show($"Impossible de contacter Roblox :\n{error.Message}", "Roblox Tracker",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            Refresh();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private bool CanRefreshFromRoblox() => !IsRefreshing;

    [RelayCommand]
    private void DeleteSession(SessionInfo session)
    {
        if (MessageBox.Show("Supprimer cette session de l'historique ?", "Roblox Tracker",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        library.DeleteSession(session.Id);
        Refresh();
    }

    [RelayCommand]
    private void DeleteGame()
    {
        if (Game is null || MessageBox.Show(
                $"Supprimer « {Game.Name} » et tout son historique de jeu ?", "Roblox Tracker",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        library.DeleteGame(UniverseId);
        nav.GoBack();
    }
}
