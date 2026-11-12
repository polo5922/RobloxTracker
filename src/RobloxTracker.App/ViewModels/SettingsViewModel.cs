using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxTracker.App.Services;
using RobloxTracker.Core.Data;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.ViewModels;

public partial class SettingsViewModel(
    SettingsService settings,
    StartupService startup,
    SessionTracker tracker,
    GameCatalogService catalog,
    BackupImportService importer,
    DiscordPresenceService discord,
    AntiAfkService antiAfk) : PageViewModel
{
    private bool _loading;

    public IReadOnlyList<int> RefreshIntervals { get; } = [15, 30, 60, 120, 240];
    public string DataDir => AppPaths.DataDir;
    public string LogsDir => AppPaths.RobloxLogsDir;

    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool closeToTray;
    [ObservableProperty] private int refreshIntervalMinutes;
    [ObservableProperty] private bool logsDirExists;
    [ObservableProperty] private string? errorText;
    [ObservableProperty] private string? importStatus;
    [ObservableProperty] private bool discordPresence;
    [ObservableProperty] private string discordAppId = "";
    [ObservableProperty] private bool antiAfkEnabled;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AntiAfkTooLong))] private int antiAfkMinMinutes;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AntiAfkTooLong))] private int antiAfkMaxMinutes;

    public bool AntiAfkTooLong => Math.Max(AntiAfkMinMinutes, AntiAfkMaxMinutes) >= AntiAfkSchedule.RobloxKickMinutes;

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_loading) startup.SetEnabled(value);
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (_loading) return;
        settings.Current.CloseToTray = value;
        settings.Save();
    }

    partial void OnRefreshIntervalMinutesChanged(int value)
    {
        if (_loading) return;
        settings.Current.RefreshIntervalMinutes = value;
        settings.Save();
    }

    partial void OnDiscordPresenceChanged(bool value)
    {
        if (_loading) return;
        settings.Current.DiscordPresence = value;
        settings.Save();
        discord.Reset();
    }

    partial void OnDiscordAppIdChanged(string value)
    {
        if (_loading) return;
        settings.Current.DiscordAppId = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        settings.Save();
        discord.Reset();
    }

    partial void OnAntiAfkEnabledChanged(bool value) => SaveAntiAfk();
    partial void OnAntiAfkMinMinutesChanged(int value) => SaveAntiAfk();
    partial void OnAntiAfkMaxMinutesChanged(int value) => SaveAntiAfk();

    private void SaveAntiAfk()
    {
        if (_loading) return;
        settings.Current.AntiAfkEnabled = AntiAfkEnabled;
        settings.Current.AntiAfkMinMinutes = AntiAfkMinMinutes;
        settings.Current.AntiAfkMaxMinutes = AntiAfkMaxMinutes;
        settings.Save();
        antiAfk.Reset();
    }

    public override void Refresh()
    {
        _loading = true;
        StartWithWindows = startup.IsEnabled;
        CloseToTray = settings.Current.CloseToTray;
        RefreshIntervalMinutes = settings.Current.RefreshIntervalMinutes;
        DiscordPresence = settings.Current.DiscordPresence;
        DiscordAppId = discord.AppId;
        AntiAfkEnabled = settings.Current.AntiAfkEnabled;
        AntiAfkMinMinutes = settings.Current.AntiAfkMinMinutes;
        AntiAfkMaxMinutes = settings.Current.AntiAfkMaxMinutes;
        LogsDirExists = Directory.Exists(AppPaths.RobloxLogsDir);

        var errors = new[] { ("Suivi des logs", tracker.LastError), ("API Roblox", catalog.LastError) }
            .Where(e => e.Item2 is not null)
            .Select(e => $"{e.Item1} : {e.Item2!.Message}");
        ErrorText = string.Join("\n", errors);
        _loading = false;
    }

    [RelayCommand]
    private void OpenDataDir() => Shell.Open(AppPaths.DataDir);

    [RelayCommand]
    private void OpenLogsDir()
    {
        if (Directory.Exists(AppPaths.RobloxLogsDir)) Shell.Open(AppPaths.RobloxLogsDir);
    }

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Dossier des sauvegardes de l'ancien tracker" };
        if (dialog.ShowDialog() != true) return;

        ImportStatus = "Import en cours…";
        try
        {
            var result = await Task.Run(() => importer.ImportFolder(dialog.FolderName));
            ImportStatus = $"{result.Sessions} sessions importées ({result.Games} jeux, {Converters.DurationConverter.Format(result.TotalTime)}) " +
                           $"depuis {result.FilesRead} fichiers. Les noms et images arrivent dans quelques instants.";
            _ = catalog.ResolvePendingAsync();
        }
        catch (Exception ex)
        {
            ImportStatus = $"Échec de l'import : {ex.Message}";
        }
    }
}
