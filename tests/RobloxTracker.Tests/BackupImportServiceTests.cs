using Microsoft.Data.Sqlite;
using RobloxTracker.Core.Data;
using RobloxTracker.Core.Models;
using RobloxTracker.Core.Services;

namespace RobloxTracker.Tests;

public sealed class BackupImportServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rt-import-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteTrackerDbFactory _db;

    public BackupImportServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new SqliteTrackerDbFactory(Path.Combine(_dir, "test.db"));
        _db.EnsureDatabase();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static long Ms(DateTime local) => new DateTimeOffset(local.ToUniversalTime()).ToUnixTimeMilliseconds();

    private void WriteBackup(string name, string days) => File.WriteAllText(Path.Combine(_dir, name), $$"""
        { "backupImports": {}, "gameStorage": { "allAccountsStorage": { "genshin": { "42": { {{days}} } } } }, "rotations": {} }
        """);

    private List<PlaySession> Sessions()
    {
        using var db = _db.Create();
        return db.Sessions.OrderBy(s => s.StartUtc).ToList();
    }

    [Fact]
    public void Daily_totals_become_one_session_per_game_chained_from_midnight()
    {
        WriteBackup("a.rtb", """
            "01-10-2026": { "characters": { "100": { "timeOnTheField": 3600 }, "200": { "timeOnTheField": 1800 } } }
            """);

        var result = new BackupImportService(_db).ImportFolder(_dir);

        Assert.Equal(2, result.Games);
        Assert.Equal(TimeSpan.FromMinutes(90), result.TotalTime);

        var sessions = Sessions();
        var midnight = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
        Assert.Equal(midnight, sessions[0].StartUtc);
        Assert.Equal(100, sessions[0].UniverseId);
        Assert.Equal(sessions[0].EndUtc, sessions[1].StartUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), sessions[1].Duration);

        using var db = _db.Create();
        Assert.Equal(2, db.Games.Count());
    }

    [Fact]
    public void Real_sessions_are_kept_and_only_the_remainder_is_synthesized()
    {
        var start = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Local);
        WriteBackup("a.rtb", $$"""
            "20-10-2026": {
              "characters": { "100": { "timeOnTheField": 4000 } },
              "sessions": [ { "id": "42-100-1", "gameId": "100", "start": {{Ms(start)}}, "end": {{Ms(start.AddHours(1))}}, "seconds": 3600 } ]
            }
            """);

        new BackupImportService(_db).ImportFolder(_dir);

        var sessions = Sessions();
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.StartUtc == start.ToUniversalTime() && s.Duration == TimeSpan.FromHours(1));
        Assert.Contains(sessions, s => s.Duration == TimeSpan.FromSeconds(400));
    }

    [Fact]
    public void Files_are_merged_and_reimport_does_not_duplicate()
    {
        WriteBackup("old.rtb", """
            "01-10-2026": { "characters": { "100": { "timeOnTheField": 600 } } },
            "02-10-2026": { "characters": { "100": { "timeOnTheField": 900 } } }
            """);
        WriteBackup("new.rtb", """
            "02-10-2026": { "characters": { "100": { "timeOnTheField": 1200 } } }
            """);

        var service = new BackupImportService(_db);
        service.ImportFolder(_dir);
        var result = service.ImportFolder(_dir);

        Assert.Equal(2, result.Sessions);
        Assert.Equal(TimeSpan.FromSeconds(600 + 1200), result.TotalTime);
        Assert.Equal(2, Sessions().Count);
    }

    [Fact]
    public void Days_already_tracked_by_the_app_are_not_imported()
    {
        using (var db = _db.Create())
        {
            db.Sessions.Add(new PlaySession
            {
                JobId = "live", PlaceId = 1,
                StartUtc = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Local).ToUniversalTime(),
            });
            db.SaveChanges();
        }
        WriteBackup("a.rtb", """
            "01-10-2026": { "characters": { "100": { "timeOnTheField": 600 } } },
            "03-10-2026": { "characters": { "100": { "timeOnTheField": 600 } } }
            """);

        var result = new BackupImportService(_db).ImportFolder(_dir);

        Assert.Equal(1, result.Sessions);
        Assert.Equal(2, Sessions().Count);
    }
}
