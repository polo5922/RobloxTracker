using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxTracker.App.Services;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.ViewModels;

public partial class HistoryViewModel(LibraryService library, NavigationService nav) : PageViewModel
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public IReadOnlyList<string> Periods { get; } = ["7 derniers jours", "30 derniers jours", "Tout l'historique"];

    [ObservableProperty] private string selectedPeriod = "7 derniers jours";
    /// <summary>En-têtes de jour (<see cref="DayHeader"/>) et sessions à plat, pour une liste virtualisée.</summary>
    [ObservableProperty] private IReadOnlyList<object> items = [];
    [ObservableProperty] private TimeSpan periodTotal;
    [ObservableProperty] private int sessionCount;

    partial void OnSelectedPeriodChanged(string value) => Refresh();

    public override void Refresh()
    {
        DateTime? since = SelectedPeriod switch
        {
            "7 derniers jours" => DateTime.Today.AddDays(-6).ToUniversalTime(),
            "30 derniers jours" => DateTime.Today.AddDays(-29).ToUniversalTime(),
            _ => null,
        };

        var sessions = library.GetSessions(sinceUtc: since);
        SessionCount = sessions.Count;
        PeriodTotal = sessions.Aggregate(TimeSpan.Zero, (acc, s) => acc + s.Duration);

        var items = new List<object>(sessions.Count + 64);
        foreach (var day in sessions.GroupBy(s => s.StartUtc.ToLocalTime().Date).OrderByDescending(g => g.Key))
        {
            items.Add(new DayHeader(DayLabel(day.Key), day.Aggregate(TimeSpan.Zero, (acc, s) => acc + s.Duration)));
            items.AddRange(day);
        }
        Items = items;
    }

    private static string DayLabel(DateTime day)
    {
        if (day == DateTime.Today) return "Aujourd'hui";
        if (day == DateTime.Today.AddDays(-1)) return "Hier";
        var label = day.ToString("dddd d MMMM yyyy", French);
        return char.ToUpper(label[0]) + label[1..];
    }

    [RelayCommand]
    private void OpenGame(long? universeId)
    {
        if (universeId is { } id) nav.OpenGame(id);
    }

    [RelayCommand]
    private void DeleteSession(SessionInfo session)
    {
        if (MessageBox.Show("Supprimer cette session de l'historique ?", "Roblox Tracker",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        library.DeleteSession(session.Id);
        Refresh();
    }
}
