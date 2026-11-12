using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxTracker.App.Converters;
using RobloxTracker.App.Services;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.ViewModels;

public partial class DashboardViewModel(StatsService stats, LibraryService library, NavigationService nav) : PageViewModel
{
    private const double ChartHeight = 150;
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    [ObservableProperty] private TimeSpan today;
    [ObservableProperty] private TimeSpan week;
    [ObservableProperty] private TimeSpan total;
    [ObservableProperty] private int gameCount;
    [ObservableProperty] private IReadOnlyList<BarItem> daily = [];
    [ObservableProperty] private IReadOnlyList<TopGameItem> topGames = [];
    [ObservableProperty] private IReadOnlyList<SessionInfo> recentSessions = [];
    [ObservableProperty] private string greeting = "";
    [ObservableProperty] private string dateText = "";

    public PeriodStatsViewModel Period { get; } = new(stats, openGame: nav.OpenGame);

    public override void Refresh()
    {
        var hour = DateTime.Now.Hour;
        Greeting = hour is >= 5 and < 18 ? "Bonjour" : "Bonsoir";
        var date = DateTime.Today.ToString("dddd d MMMM yyyy", French);
        DateText = char.ToUpper(date[0], French) + date[1..];

        var games = stats.GetGameStats();
        Today = games.Aggregate(TimeSpan.Zero, (acc, g) => acc + g.Today);
        Week = games.Aggregate(TimeSpan.Zero, (acc, g) => acc + g.Last7Days);
        Total = games.Aggregate(TimeSpan.Zero, (acc, g) => acc + g.Total);
        GameCount = games.Count;

        var days = stats.GetDailyTotals(14);
        var maxDay = days.Max(d => d.Total.TotalSeconds);
        Daily = days.Select(d => new BarItem(
            d.Date.ToString("ddd d", French),
            $"{d.Date.ToString("dddd d MMMM", French)} : {DurationConverter.Format(d.Total)}",
            maxDay <= 0 ? 0 : Math.Max(d.Total.TotalSeconds > 0 ? 3 : 0, d.Total.TotalSeconds / maxDay * ChartHeight),
            d.Date == DateTime.Today,
            DurationConverter.Short(d.Total))).ToList();

        var top = games.Where(g => g.Total > TimeSpan.Zero).OrderByDescending(g => g.Total).Take(6).ToList();
        var maxGame = top.Count == 0 ? 1 : top[0].Total.TotalSeconds;
        TopGames = top.Select((g, i) => new TopGameItem(new GameCardItem(g, library), g.Total.TotalSeconds / maxGame, i + 1)).ToList();

        RecentSessions = library.GetSessions(limit: 8);
        Period.Refresh();
    }

    [RelayCommand]
    private void OpenGame(long? universeId)
    {
        if (universeId is { } id) nav.OpenGame(id);
    }
}
