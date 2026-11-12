namespace RobloxTracker.Core.Data;

public static class AppPaths
{
    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string DataDir { get; } = Path.Combine(LocalAppData, "RobloxTracker");

    public static string DatabasePath => Path.Combine(DataDir, "tracker.db");

    public static string ImagesDir => Path.Combine(DataDir, "images");

    public static string BackupsDir => Path.Combine(DataDir, "sauvegardes");

    public static string SettingsPath => Path.Combine(DataDir, "settings.json");

    public static string RobloxLogsDir { get; } = Path.Combine(LocalAppData, "Roblox", "logs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ImagesDir);
    }
}
