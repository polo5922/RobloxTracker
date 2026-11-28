using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using RobloxTracker.Core.Data;

namespace RobloxTracker.App.Services;

public sealed class AppSettings
{
    public bool CloseToTray { get; set; } = true;
    public int RefreshIntervalMinutes { get; set; } = 30;
    public bool TrayHintShown { get; set; }
    public bool DiscordPresence { get; set; } = true;
    public string? DiscordAppId { get; set; }
    public bool AntiAfkEnabled { get; set; }
    public int AntiAfkMinMinutes { get; set; } = 10;
    public int AntiAfkMaxMinutes { get; set; } = 15;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public AppSettings Current { get; }

    public SettingsService()
    {
        try
        {
            Current = File.Exists(AppPaths.SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath)) ?? new()
                : new();
        }
        catch (JsonException)
        {
            Current = new();
        }
    }

    public void Save() => File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(Current, Json));
}

/// <summary>Lancement automatique avec Windows via la clé Run de l'utilisateur courant.</summary>
public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RobloxTracker";

    private static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>Navigation entre pages, découplée des ViewModels.</summary>
public sealed class NavigationService
{
    public event Action<long>? OpenGameRequested;
    public event Action<DateTime, long?>? OpenDayRequested;
    public event Action? BackRequested;

    public void OpenGame(long universeId) => OpenGameRequested?.Invoke(universeId);

    /// <summary>Ouvre le détail d'une journée, limité à un jeu si <paramref name="universeId"/> est renseigné.</summary>
    public void OpenDay(DateTime day, long? universeId = null) => OpenDayRequested?.Invoke(day.Date, universeId);

    public void GoBack() => BackRequested?.Invoke();
}

public static class Shell
{
    public static void Open(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    public static void LaunchRobloxPlace(long placeId) =>
        Open($"roblox://experiences/start?placeId={placeId}");
}
