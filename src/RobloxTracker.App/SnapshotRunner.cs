using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RobloxTracker.App.ViewModels;

namespace RobloxTracker.App;

/// <summary>
/// Mode <c>--snapshot &lt;dossier&gt;</c> : rend chaque page hors écran en PNG puis quitte.
/// Sert à vérifier l'interface sans interagir avec le bureau.
/// </summary>
internal static class SnapshotRunner
{
    public static async Task RunAsync(Window window, MainViewModel vm, string outputDir, Func<long?> firstGameId, Action<long> openGame)
    {
        Directory.CreateDirectory(outputDir);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = 0;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Show();

        for (var i = 0; i < vm.NavItems.Count; i++)
        {
            vm.SelectedNav = vm.NavItems[i];
            await CaptureAsync(window, Path.Combine(outputDir, $"{i + 1}-{Slug(vm.NavItems[i].Title)}.png"));
            if (vm.NavItems[i].Page is DashboardViewModel dashboard)
            {
                await CapturePeriodAsync(window, dashboard.Period, Path.Combine(outputDir, "1b-periode"));
                // Détail du jour le plus joué des 14 derniers jours.
                if (dashboard.Daily.Where(b => b.BarHeight > 0).MaxBy(b => b.BarHeight) is { } bar)
                {
                    dashboard.OpenDayCommand.Execute(bar);
                    await CaptureAsync(window, Path.Combine(outputDir, "1c-journee.png"));
                }
            }
        }

        if (firstGameId() is { } id)
        {
            openGame(id);
            await CaptureAsync(window, Path.Combine(outputDir, "7-detail.png"));
            if (vm.CurrentPage is GameDetailViewModel detail)
                await CapturePeriodAsync(window, detail.Period, Path.Combine(outputDir, "7b-periode"));
        }
    }

    /// <summary>Fait défiler jusqu'au panneau « Statistiques par période » et le capture en vue mois puis année.</summary>
    private static async Task CapturePeriodAsync(Window window, PeriodStatsViewModel period, string pathPrefix)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (FindDescendant<Views.PeriodStatsView>(window) is { } view)
            view.BringIntoView(new Rect(0, 0, view.ActualWidth, view.ActualHeight + 400));
        await CaptureAsync(window, pathPrefix + "-mois.png");
        period.PreviousCommand.Execute(null); // le mois en cours peut être vide
        await CaptureAsync(window, pathPrefix + "-mois-precedent.png");
        period.IsYearMode = true;
        await CaptureAsync(window, pathPrefix + "-annee.png");
        period.IsYearMode = false;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }

    private static async Task CaptureAsync(Window window, string path)
    {
        // Laisse le temps aux images (décodées en arrière-plan) et aux animations d'arriver.
        await Task.Delay(1200);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

        var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(content);
        var bitmap = new RenderTargetBitmap(
            (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static string Slug(string title) =>
        new(title.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
}
