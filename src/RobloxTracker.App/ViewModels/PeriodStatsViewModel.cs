using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxTracker.App.Converters;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.ViewModels;

public sealed record PeriodGameItem(long UniverseId, string Name, string? IconPath, TimeSpan Total, double Ratio, int Rank);

/// <summary>
/// Panneau « Statistiques par période » : choix d'un mois ou d'une année, graphe et chiffres clés.
/// Partagé par le tableau de bord (tous les jeux) et la fiche d'un jeu (<paramref name="universeId"/>).
/// Un clic sur une barre ouvre la journée (vue mois) ou le mois (vue année).
/// </summary>
public partial class PeriodStatsViewModel(
    StatsService stats,
    long? universeId = null,
    Action<long>? openGame = null,
    Action<DateTime, long?>? openDay = null) : ObservableObject
{
    private const double ChartHeight = 150;
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private bool _loading;

    public bool ShowTopGames => universeId is null;

    public IReadOnlyList<FilterOption> Months { get; } = Enumerable.Range(1, 12)
        .Select(m => new FilterOption(m, Capitalize(French.DateTimeFormat.GetMonthName(m))))
        .ToList();

    [ObservableProperty] private bool isYearMode;
    [ObservableProperty] private IReadOnlyList<int> years = [DateTime.Today.Year];
    [ObservableProperty] private int selectedYear = DateTime.Today.Year;
    [ObservableProperty] private FilterOption? selectedMonth;

    [ObservableProperty] private string periodTitle = "";
    [ObservableProperty] private IReadOnlyList<BarItem> bars = [];
    [ObservableProperty] private TimeSpan total;
    [ObservableProperty] private int sessionCount;
    [ObservableProperty] private int daysPlayed;
    [ObservableProperty] private int daysInPeriod;
    [ObservableProperty] private TimeSpan averagePerDay;
    [ObservableProperty] private string bestText = "";
    [ObservableProperty] private string? deltaText;
    [ObservableProperty] private bool deltaUp;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private IReadOnlyList<PeriodGameItem> topGames = [];

    partial void OnIsYearModeChanged(bool value) => Load();
    partial void OnSelectedYearChanged(int value) => Load();
    partial void OnSelectedMonthChanged(FilterOption? value) => Load();

    /// <summary>Recharge la période affichée (appelé par la page parente).</summary>
    public void Refresh()
    {
        _loading = true;
        var current = DateTime.Today.Year;
        var first = Math.Min(stats.GetFirstYear(universeId), current);
        Years = Enumerable.Range(first, current - first + 1).Reverse().ToList();
        if (!Years.Contains(SelectedYear)) SelectedYear = current;
        SelectedMonth ??= Months[DateTime.Today.Month - 1];
        _loading = false;
        Load();
    }

    [RelayCommand]
    private void Previous() => Shift(-1);

    [RelayCommand]
    private void Next() => Shift(1);

    [RelayCommand]
    private void OpenGame(long id) => openGame?.Invoke(id);

    [RelayCommand]
    private void SelectBar(BarItem? bar)
    {
        if (bar?.Date is not { } date) return;
        if (!IsYearMode)
        {
            openDay?.Invoke(date, universeId);
            return;
        }

        _loading = true;
        SelectedYear = date.Year;
        SelectedMonth = Months[date.Month - 1];
        IsYearMode = false;
        _loading = false;
        Load();
    }

    private void Shift(int delta)
    {
        var start = new DateTime(SelectedYear, SelectedMonth?.Id ?? 1, 1);
        start = IsYearMode ? start.AddYears(delta) : start.AddMonths(delta);
        if (start > DateTime.Today || start.Year < Years[^1]) return;

        _loading = true;
        SelectedYear = start.Year;
        SelectedMonth = Months[start.Month - 1];
        _loading = false;
        Load();
    }

    private void Load()
    {
        if (_loading || SelectedMonth is null) return;

        var p = stats.GetPeriodStats(SelectedYear, IsYearMode ? null : SelectedMonth.Id, universeId);
        PeriodTitle = IsYearMode ? SelectedYear.ToString() : $"{SelectedMonth.Name} {SelectedYear}";

        var max = p.Buckets.Count == 0 ? 0 : p.Buckets.Max(b => b.Total.TotalSeconds);
        var today = DateTime.Today;
        Bars = p.Buckets.Select(b => new BarItem(
            IsYearMode ? Capitalize(b.Date.ToString("MMM", French).TrimEnd('.')) : b.Date.Day.ToString(),
            (IsYearMode ? Capitalize(b.Date.ToString("MMMM yyyy", French)) : b.Date.ToString("dddd d MMMM", French))
                + $" : {DurationConverter.Format(b.Total)}"
                + (IsYearMode ? "\nClique pour voir le mois" : openDay is null ? "" : "\nClique pour voir la journée"),
            max <= 0 ? 0 : Math.Max(b.Total.TotalSeconds > 0 ? 3 : 0, b.Total.TotalSeconds / max * ChartHeight),
            IsYearMode ? b.Date.Year == today.Year && b.Date.Month == today.Month : b.Date == today,
            IsYearMode ? DurationConverter.Short(b.Total) : "",
            b.Date)).ToList();

        Total = p.Total;
        SessionCount = p.SessionCount;
        DaysPlayed = p.DaysPlayed;
        DaysInPeriod = (int)(p.End - p.Start).TotalDays;
        AveragePerDay = p.DaysPlayed == 0 ? TimeSpan.Zero : p.Total / p.DaysPlayed;
        IsEmpty = p.Total == TimeSpan.Zero;

        var best = p.Buckets.MaxBy(b => b.Total);
        BestText = best is null || best.Total == TimeSpan.Zero
            ? ""
            : (IsYearMode ? "Meilleur mois : " + best.Date.ToString("MMMM", French) : "Meilleur jour : " + best.Date.ToString("dddd d", French))
              + " · " + DurationConverter.Format(best.Total);

        if (p.PreviousTotal > TimeSpan.Zero)
        {
            var change = (p.Total.TotalSeconds - p.PreviousTotal.TotalSeconds) / p.PreviousTotal.TotalSeconds;
            DeltaUp = change >= 0;
            DeltaText = $"{(DeltaUp ? "+" : "−")}{Math.Abs(change):P0} vs {(IsYearMode ? "année" : "mois")} précédent{(IsYearMode ? "e" : "")}";
        }
        else
        {
            DeltaText = null;
        }

        var topMax = p.TopGames.Count == 0 ? 1 : p.TopGames[0].Total.TotalSeconds;
        TopGames = p.TopGames.Select((g, i) => new PeriodGameItem(
            g.Game.UniverseId, g.Game.Name, g.Game.IconPath, g.Total, g.Total.TotalSeconds / topMax, i + 1)).ToList();
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], French) + s[1..];
}
