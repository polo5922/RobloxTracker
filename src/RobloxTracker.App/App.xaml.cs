using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using RobloxTracker.App.Services;
using RobloxTracker.App.ViewModels;
using RobloxTracker.Core.Data;
using RobloxTracker.Core.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace RobloxTracker.App;

public partial class App : Application
{
    private const string InstanceName = "RobloxTracker.SingleInstance";

    private readonly CancellationTokenSource _cts = new();
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private ServiceProvider? _services;
    private Forms.NotifyIcon? _tray;
    private MainWindow? _window;
    private MainViewModel? _mainVm;
    private bool _exiting;
    private DispatcherTimer? _dataChangedTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var snapshotIndex = Array.FindIndex(e.Args, a => a.Equals("--snapshot", StringComparison.OrdinalIgnoreCase));
        if (snapshotIndex >= 0 && snapshotIndex + 1 < e.Args.Length)
        {
            RunSnapshot(e.Args[snapshotIndex + 1]);
            return;
        }

        // Une seule instance : la seconde demande simplement à la première d'afficher sa fenêtre.
        _instanceMutex = new Mutex(true, InstanceName + ".Mutex", out var isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        if (!isFirst)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            MessageBox.Show($"Une erreur inattendue s'est produite :\n{args.Exception.Message}", "Roblox Tracker",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("fr-FR")));

        AppPaths.EnsureCreated();
        ProtectDatabase();
        _services = ConfigureServices();

        var settings = _services.GetRequiredService<SettingsService>();
        var tracker = _services.GetRequiredService<SessionTracker>();
        var catalog = _services.GetRequiredService<GameCatalogService>();
        _mainVm = _services.GetRequiredService<MainViewModel>();

        // --import "<dossier>" : importe les sauvegardes de l'ancien tracker au démarrage.
        var importIndex = Array.FindIndex(e.Args, a => a.Equals("--import", StringComparison.OrdinalIgnoreCase));
        if (importIndex >= 0 && importIndex + 1 < e.Args.Length)
        {
            try
            {
                _services.GetRequiredService<BackupImportService>().ImportFolder(e.Args[importIndex + 1]);
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        _window = new MainWindow
        {
            DataContext = _mainVm,
            InterceptClose = () => !_exiting && settings.Current.CloseToTray,
        };
        _window.Closed += (_, _) => ExitApp();
        // Fenêtre créée même si l'app démarre réduite : l'installateur peut ainsi lui demander de se fermer.
        new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();
        SessionEnding += (_, _) => ExitApp();
        _window.IsVisibleChanged += (_, _) =>
        {
            if (!_window.IsVisible) ShowTrayHintOnce(settings);
        };

        CreateTray();

        tracker.DataChanged += (_, _) =>
        {
            _ = catalog.ResolvePendingAsync(_cts.Token);
            NotifyDataChanged();
        };
        catalog.DataChanged += (_, _) => NotifyDataChanged();

        _ = Task.Run(() => tracker.RunAsync(_cts.Token));
        _ = Task.Run(() => RefreshLoopAsync(catalog, settings, _cts.Token));
        _ = Task.Run(WaitForShowSignal);

        if (!e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
            ShowWindow();
    }

    /// <summary>
    /// Restaure la dernière sauvegarde si la base a disparu, puis fait une copie de sécurité
    /// (à chaque nouvelle version, avant la mise à jour du schéma, et au plus une fois par jour).
    /// </summary>
    private static void ProtectDatabase()
    {
        try
        {
            DatabaseBackup.RestoreIfMissing(AppPaths.DatabasePath, AppPaths.BackupsDir);
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0";
            DatabaseBackup.CreateIfNeeded(AppPaths.DatabasePath, AppPaths.BackupsDir, version);
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    /// <summary>Rend les pages en PNG (sans tracker ni icône de notification) puis quitte.</summary>
    private async void RunSnapshot(string outputDir)
    {
        try
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("fr-FR")));
            _services = ConfigureServices();
            _mainVm = _services.GetRequiredService<MainViewModel>();
            var stats = _services.GetRequiredService<StatsService>();
            var nav = _services.GetRequiredService<NavigationService>();
            var window = new MainWindow { DataContext = _mainVm };

            await SnapshotRunner.RunAsync(window, _mainVm, outputDir,
                () => stats.GetGameStats().OrderByDescending(g => g.Total).FirstOrDefault()?.Game.UniverseId,
                nav.OpenGame);
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
        Shutdown();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        var dbFactory = new SqliteTrackerDbFactory(AppPaths.DatabasePath);
        dbFactory.EnsureDatabase();
        services.AddSingleton<ITrackerDbFactory>(dbFactory);

        services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxTracker/1.0");
            return new RobloxApiClient(http);
        });
        services.AddSingleton(sp => new GameCatalogService(
            sp.GetRequiredService<ITrackerDbFactory>(), sp.GetRequiredService<RobloxApiClient>(), AppPaths.ImagesDir));
        services.AddSingleton(sp => new SessionTracker(
            sp.GetRequiredService<ITrackerDbFactory>(), AppPaths.RobloxLogsDir, SessionTracker.IsRobloxPlayerRunning));
        services.AddSingleton<StatsService>();
        services.AddSingleton<LibraryService>();
        services.AddSingleton<BackupImportService>();

        services.AddSingleton<SettingsService>();
        services.AddSingleton<StartupService>();
        services.AddSingleton<DiscordPresenceService>();
        services.AddSingleton<AntiAfkService>();
        services.AddSingleton<NavigationService>();

        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<UpdatesViewModel>();
        services.AddSingleton<OrganizeViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<Func<long, GameDetailViewModel>>(sp => id => new GameDetailViewModel(
            id,
            sp.GetRequiredService<LibraryService>(),
            sp.GetRequiredService<StatsService>(),
            sp.GetRequiredService<GameCatalogService>(),
            sp.GetRequiredService<NavigationService>()));
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }

    private async Task RefreshLoopAsync(GameCatalogService catalog, SettingsService settings, CancellationToken ct)
    {
        try
        {
            await catalog.ResolvePendingAsync(ct);
            while (!ct.IsCancellationRequested)
            {
                await catalog.RefreshAllAsync(ct);
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(5, settings.Current.RefreshIntervalMinutes)), ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void WaitForShowSignal()
    {
        while (!_cts.IsCancellationRequested)
        {
            if (_showSignal!.WaitOne(TimeSpan.FromSeconds(1)))
                Dispatcher.BeginInvoke(ShowWindow);
        }
    }

    /// <summary>
    /// Regroupe les notifications rapprochées (ex. rafraîchissement du catalogue par lots)
    /// en un seul rechargement de l'interface.
    /// </summary>
    private void NotifyDataChanged() =>
        Dispatcher.BeginInvoke(() =>
        {
            if (_exiting) return;
            if (_dataChangedTimer is null)
            {
                _dataChangedTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
                _dataChangedTimer.Tick += (_, _) =>
                {
                    _dataChangedTimer.Stop();
                    if (_exiting) return;
                    RefreshData();
                };
            }
            if (_dataChangedTimer.IsEnabled) return;

            // Première notification après un moment calme (ex. début de partie) : affichage immédiat.
            if (DateTime.UtcNow - _lastDataRefreshUtc >= _dataChangedTimer.Interval)
                RefreshData();
            else
                _dataChangedTimer.Start();
        });

    private DateTime _lastDataRefreshUtc;

    private void RefreshData()
    {
        _lastDataRefreshUtc = DateTime.UtcNow;
        _mainVm?.OnDataChanged();
        UpdateTrayText();
    }

    private void ShowWindow()
    {
        if (_window is null || _exiting) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    // ----- Zone de notification -----

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir Roblox Tracker", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitApp());

        _tray = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowWindow();
        UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        if (_tray is null || _mainVm is null) return;
        var text = _mainVm.StatusText;
        _tray.Text = text.Length > 63 ? text[..62] + "…" : text;
    }

    private void ShowTrayHintOnce(SettingsService settings)
    {
        if (_exiting || _tray is null || settings.Current.TrayHintShown) return;
        settings.Current.TrayHintShown = true;
        settings.Save();
        _tray.ShowBalloonTip(4000, "Roblox Tracker continue en arrière-plan",
            "Le suivi du temps de jeu reste actif. Double-clique sur l'icône pour rouvrir, clic droit pour quitter.",
            Forms.ToolTipIcon.Info);
    }

    /// <summary>Icône de l'application, à la taille des petites icônes de Windows (prend en compte le DPI).</summary>
    private static Drawing.Icon CreateTrayIcon()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    // ----- Fermeture -----

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _cts.Cancel();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        if (_instanceMutex is not null)
        {
            try
            {
                DatabaseBackup.Checkpoint(AppPaths.DatabasePath);
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }
        _instanceMutex?.Dispose();
        _showSignal?.Dispose();
        base.OnExit(e);
    }

    private static void LogError(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "errors.log"), $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch (IOException)
        {
        }
    }
}
