using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxTracker.App.Services;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.ViewModels;

public partial class UpdatesViewModel(LibraryService library, GameCatalogService catalog, NavigationService nav) : PageViewModel
{
    private List<UpdateInfo> _all = [];

    [ObservableProperty] private bool favoritesOnly;
    [ObservableProperty] private bool newOnly;
    [ObservableProperty] private IReadOnlyList<UpdateInfo> items = [];
    [ObservableProperty] private int newCount;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    private bool isChecking;

    partial void OnFavoritesOnlyChanged(bool value) => ApplyFilter();
    partial void OnNewOnlyChanged(bool value) => ApplyFilter();

    public override void Refresh()
    {
        _all = library.GetUpdates();
        NewCount = _all.Count(u => u.IsNewSinceLastPlay);
        ApplyFilter();
    }

    private void ApplyFilter() =>
        Items = _all
            .Where(u => !FavoritesOnly || u.IsFavorite)
            .Where(u => !NewOnly || u.IsNewSinceLastPlay)
            .ToList();

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNowAsync()
    {
        IsChecking = true;
        try
        {
            await catalog.RefreshAllAsync();
            if (catalog.LastError is { } error)
                MessageBox.Show($"Impossible de contacter Roblox :\n{error.Message}", "Roblox Tracker",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            Refresh();
        }
        finally
        {
            IsChecking = false;
        }
    }

    private bool CanCheckNow() => !IsChecking;

    [RelayCommand]
    private void OpenGame(long universeId) => nav.OpenGame(universeId);
}
