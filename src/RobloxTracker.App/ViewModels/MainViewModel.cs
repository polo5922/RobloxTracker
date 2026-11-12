using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxTracker.App.Services;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private readonly NavigationService _nav;
    private readonly DiscordPresenceService _discord;
    private readonly AntiAfkService _antiAfk;
    private readonly Func<long, GameDetailViewModel> _detailFactory;
    private readonly Stack<PageViewModel> _back = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _ticks;

    public MainViewModel(
        DashboardViewModel dashboard,
        LibraryViewModel library,
        HistoryViewModel history,
        UpdatesViewModel updates,
        OrganizeViewModel organize,
        SettingsViewModel settings,
        LibraryService libraryService,
        NavigationService nav,
        DiscordPresenceService discord,
        AntiAfkService antiAfk,
        Func<long, GameDetailViewModel> detailFactory)
    {
        _library = libraryService;
        _nav = nav;
        _discord = discord;
        _antiAfk = antiAfk;
        _detailFactory = detailFactory;
        Updates = updates;

        NavItems =
        [
            new("Tableau de bord", "", dashboard),
            new("Bibliothèque", "", library),
            new("Historique", "", history),
            new("Mises à jour", "", updates),
            new("Catégories & groupes", "", organize),
            new("Paramètres", "", settings),
        ];

        nav.OpenGameRequested += OpenGame;
        nav.BackRequested += GoBack;

        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        SelectedNav = NavItems[0];
        OnDataChanged();
    }

    public IReadOnlyList<NavEntry> NavItems { get; }
    public UpdatesViewModel Updates { get; }

    [ObservableProperty] private NavEntry? selectedNav;
    [ObservableProperty] private PageViewModel? currentPage;
    [ObservableProperty] private SessionInfo? activeSession;
    [ObservableProperty] private string elapsedText = "";

    /// <summary>Texte court pour l'infobulle de l'icône de notification.</summary>
    public string StatusText => ActiveSession is { } s
        ? $"En jeu : {s.GameName} ({ElapsedText})"
        : "Roblox Tracker — aucun jeu en cours";

    partial void OnSelectedNavChanged(NavEntry? value)
    {
        if (value is null) return;
        _back.Clear();
        Show(value.Page);
    }

    /// <summary>Appelé (sur le thread UI) quand le tracker ou le catalogue a modifié la base.</summary>
    public void OnDataChanged()
    {
        ActiveSession = _library.GetActiveSession();
        _discord.Update(ActiveSession);
        _antiAfk.Update(ActiveSession);
        UpdateElapsed();
        CurrentPage?.Refresh();
        Updates.Refresh();
    }

    private void Show(PageViewModel page)
    {
        page.Refresh();
        CurrentPage = page;
    }

    private void OpenGame(long universeId)
    {
        if (CurrentPage is GameDetailViewModel current && current.UniverseId == universeId) return;
        if (CurrentPage is not null) _back.Push(CurrentPage);
        Show(_detailFactory(universeId));
    }

    private void GoBack() => Show(_back.Count > 0 ? _back.Pop() : SelectedNav?.Page ?? NavItems[0].Page);

    [RelayCommand]
    private void OpenActiveGame()
    {
        if (ActiveSession?.UniverseId is { } id) _nav.OpenGame(id);
    }

    private void Tick()
    {
        UpdateElapsed();
        // Pendant une partie, les totaux affichés avancent une fois par minute.
        if (ActiveSession is not null && ++_ticks % 60 == 0) CurrentPage?.Refresh();
    }

    private void UpdateElapsed()
    {
        ElapsedText = ActiveSession is { } s ? s.Duration.ToString(@"hh\:mm\:ss") : "";
        OnPropertyChanged(nameof(StatusText));
    }
}
