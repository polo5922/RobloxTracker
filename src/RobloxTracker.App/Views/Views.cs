using System.Windows;
using System.Windows.Controls;
using RobloxTracker.App.ViewModels;

namespace RobloxTracker.App.Views;

// Les vues n'ont presque pas de logique : tout passe par les bindings vers leur ViewModel.

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();
}

public partial class LibraryView : UserControl
{
    private const double CardWidth = 244; // largeur de carte + marge
    private const double ScrollBarWidth = 18;

    public LibraryView() => InitializeComponent();

    // Exception à la règle : la grille est découpée en lignes virtualisées, il faut donc connaître la largeur.
    private void RowsList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is LibraryViewModel vm)
            vm.Columns = Math.Max(1, (int)((e.NewSize.Width - ScrollBarWidth) / CardWidth));
    }
}

public partial class GameDetailView : UserControl
{
    public GameDetailView() => InitializeComponent();
}

public partial class DayDetailView : UserControl
{
    public DayDetailView() => InitializeComponent();
}

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();
}

public partial class UpdatesView : UserControl
{
    public UpdatesView() => InitializeComponent();
}

public partial class OrganizeView : UserControl
{
    public OrganizeView() => InitializeComponent();
}

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}

public partial class PeriodStatsView : UserControl
{
    public PeriodStatsView() => InitializeComponent();
}
