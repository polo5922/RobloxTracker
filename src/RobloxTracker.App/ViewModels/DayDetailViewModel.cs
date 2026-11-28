using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxTracker.App.Converters;
using RobloxTracker.App.Services;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.ViewModels;

/// <summary>
/// Détail d'une journée : temps par heure, jeux joués et sessions.
/// Limité au jeu <paramref name="universeId"/> s'il est renseigné (ouvert depuis la fiche d'un jeu).
/// </summary>
public partial class DayDetailViewModel(
    DateTime day,
    long? universeId,
    StatsService stats,
    LibraryService library,
    NavigationService nav) : PageViewModel
{
    private const double ChartHeight = 150;
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public long? UniverseId { get; } = universeId;
    public bool ShowGames => UniverseId is null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private DateTime date = day.Date;

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string subtitle = "";
    [ObservableProperty] private TimeSpan total;
    [ObservableProperty] private int sessionCount;
    [ObservableProperty] private TimeSpan longestSession;
    [ObservableProperty] private string rangeText = "—";
    [ObservableProperty] private string? deltaText;
    [ObservableProperty] private bool deltaUp;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private IReadOnlyList<BarItem> hours = [];
    [ObservableProperty] private IReadOnlyList<PeriodGameItem> games = [];
    [ObservableProperty] private IReadOnlyList<SessionInfo> sessions = [];

    partial void OnDateChanged(DateTime value) => Refresh();

    public override void Refresh()
    {
        var d = stats.GetDayStats(Date, UniverseId);
        var today = DateTime.Today;

        var text = Date.ToString("dddd d MMMM yyyy", French);
        Title = char.ToUpper(text[0], French) + text[1..];
        var relative = Date == today ? "Aujourd'hui" : Date == today.AddDays(-1) ? "Hier" : null;
        var gameName = UniverseId is { } id ? library.GetGame(id)?.Name : null;
        Subtitle = string.Join(" · ", new[] { relative, gameName }.Where(x => x is not null));

        Total = d.Total;
        SessionCount = d.SessionCount;
        IsEmpty = d.Total == TimeSpan.Zero;
        RangeText = d.FirstStartUtc is { } first && d.LastEndUtc is { } last
            ? $"{first.ToLocalTime():HH:mm} → {(last >= Date.AddDays(1).ToUniversalTime() ? "minuit" : last.ToLocalTime().ToString("HH:mm"))}"
            : "—";

        // Comparaison avec la moyenne des 7 jours précédents (jours sans partie compris).
        var average = d.PreviousWeekAverage;
        if (average > TimeSpan.Zero && d.Total > TimeSpan.Zero)
        {
            var change = (d.Total.TotalSeconds - average.TotalSeconds) / average.TotalSeconds;
            DeltaUp = change >= 0;
            DeltaText = $"{(DeltaUp ? "+" : "−")}{Math.Abs(change):P0} vs moyenne des 7 jours précédents";
        }
        else
        {
            DeltaText = null;
        }

        // Une heure pleine de jeu = barre pleine.
        var currentHour = Date == today ? DateTime.Now.Hour : -1;
        Hours = d.Hours.Select((t, h) => new BarItem(
            h % 3 == 0 ? $"{h} h" : "",
            $"{h} h – {h + 1} h : {DurationConverter.Format(t)}",
            t <= TimeSpan.Zero ? 0 : Math.Max(3, Math.Min(1, t.TotalMinutes / 60) * ChartHeight),
            h == currentHour)).ToList();

        var topMax = d.Games.Count == 0 ? 1 : d.Games[0].Total.TotalSeconds;
        Games = d.Games.Select((g, i) => new PeriodGameItem(
            g.Game.UniverseId, g.Game.Name, g.Game.IconPath, g.Total, g.Total.TotalSeconds / topMax, i + 1)).ToList();

        var (startUtc, endUtc) = (Date.ToUniversalTime(), Date.AddDays(1).ToUniversalTime());
        Sessions = library.GetSessions(sinceUtc: startUtc, untilUtc: endUtc, universeId: UniverseId)
            .OrderBy(s => s.StartUtc)
            .ToList();
        LongestSession = Sessions.Count == 0 ? TimeSpan.Zero : Sessions.Max(s => s.Duration);
    }

    [RelayCommand]
    private void Back() => nav.GoBack();

    [RelayCommand]
    private void Previous() => Date = Date.AddDays(-1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Date = Date.AddDays(1);

    private bool CanGoNext() => Date < DateTime.Today;

    [RelayCommand]
    private void Today() => Date = DateTime.Today;

    [RelayCommand]
    private void OpenGame(long? id)
    {
        if (id is { } universe) nav.OpenGame(universe);
    }
}
