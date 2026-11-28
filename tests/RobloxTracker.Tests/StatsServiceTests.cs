using Microsoft.Data.Sqlite;
using RobloxTracker.Core.Data;
using RobloxTracker.Core.Models;
using RobloxTracker.Core.Services;

namespace RobloxTracker.Tests;

public sealed class StatsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rt-stats-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteTrackerDbFactory _db;

    public StatsServiceTests()
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

    /// <summary>Ajoute une session définie en heure locale.</summary>
    private void AddSession(long universeId, DateTime localStart, TimeSpan duration)
    {
        using var db = _db.Create();
        if (db.Games.Find(universeId) is null) db.Games.Add(new Game { UniverseId = universeId, Name = $"Jeu {universeId}" });
        db.Sessions.Add(new PlaySession
        {
            PlaceId = universeId,
            JobId = Guid.NewGuid().ToString(),
            UniverseId = universeId,
            StartUtc = localStart.ToUniversalTime(),
            EndUtc = (localStart + duration).ToUniversalTime(),
        });
        db.SaveChanges();
    }

    [Fact]
    public void Overlap_counts_only_the_part_inside_the_window()
    {
        var start = new DateTime(2026, 10, 29, 23, 30, 0);
        var end = new DateTime(2026, 10, 30, 0, 45, 0);
        var dayStart = new DateTime(2026, 10, 30);

        Assert.Equal(TimeSpan.FromMinutes(45), StatsService.Overlap(start, end, dayStart, dayStart.AddDays(1)));
        Assert.Equal(TimeSpan.FromMinutes(30), StatsService.Overlap(start, end, dayStart.AddDays(-1), dayStart));
        Assert.Equal(TimeSpan.Zero, StatsService.Overlap(start, end, dayStart.AddDays(1), dayStart.AddDays(2)));
    }

    [Fact]
    public void Month_stats_have_one_bucket_per_day_and_split_sessions_at_month_boundaries()
    {
        AddSession(1, new DateTime(2025, 9, 30, 23, 0, 0), TimeSpan.FromHours(2)); // 1 h en septembre, 1 h en octobre
        AddSession(1, new DateTime(2025, 10, 5, 14, 0, 0), TimeSpan.FromMinutes(30));
        AddSession(2, new DateTime(2025, 10, 5, 20, 0, 0), TimeSpan.FromHours(3));

        var stats = new StatsService(_db).GetPeriodStats(2025, 10);

        Assert.Equal(31, stats.Buckets.Count);
        Assert.Equal(TimeSpan.FromHours(4.5), stats.Total);
        Assert.Equal(TimeSpan.FromHours(3.5), stats.Buckets[4].Total);
        Assert.Equal(3, stats.SessionCount);
        Assert.Equal(2, stats.DaysPlayed);
        Assert.Equal(TimeSpan.FromHours(1), stats.PreviousTotal);
        Assert.Equal([2L, 1L], stats.TopGames.Select(g => g.Game.UniverseId));
    }

    [Fact]
    public void Year_stats_have_one_bucket_per_month_and_can_be_limited_to_one_game()
    {
        AddSession(1, new DateTime(2025, 1, 10, 12, 0, 0), TimeSpan.FromHours(1));
        AddSession(1, new DateTime(2025, 6, 10, 12, 0, 0), TimeSpan.FromHours(2));
        AddSession(2, new DateTime(2025, 6, 11, 12, 0, 0), TimeSpan.FromHours(5));
        AddSession(1, new DateTime(2024, 6, 11, 12, 0, 0), TimeSpan.FromHours(4));

        var stats = new StatsService(_db);
        var year = stats.GetPeriodStats(2025, null, universeId: 1);

        Assert.Equal(12, year.Buckets.Count);
        Assert.Equal(TimeSpan.FromHours(3), year.Total);
        Assert.Equal(TimeSpan.FromHours(2), year.Buckets[5].Total);
        Assert.Equal(TimeSpan.FromHours(4), year.PreviousTotal);
        Assert.Empty(year.TopGames);
        Assert.Equal(2024, stats.GetFirstYear(1));
        Assert.Equal(2025, stats.GetFirstYear(2));
    }

    [Fact]
    public void Day_stats_split_by_hour_and_game_and_clip_sessions_at_midnight()
    {
        AddSession(1, new DateTime(2025, 10, 4, 23, 30, 0), TimeSpan.FromHours(1));     // 30 min le 5
        AddSession(1, new DateTime(2025, 10, 5, 14, 15, 0), TimeSpan.FromMinutes(90));  // 14 h : 45 min, 15 h : 45 min
        AddSession(2, new DateTime(2025, 10, 5, 20, 0, 0), TimeSpan.FromHours(3));      // 20 h → 23 h
        AddSession(2, new DateTime(2025, 10, 6, 10, 0, 0), TimeSpan.FromHours(1));      // autre jour

        var stats = new StatsService(_db);
        var day = stats.GetDayStats(new DateTime(2025, 10, 5, 18, 0, 0));

        Assert.Equal(new DateTime(2025, 10, 5), day.Date);
        Assert.Equal(TimeSpan.FromMinutes(30 + 90 + 180), day.Total);
        Assert.Equal(3, day.SessionCount);
        Assert.Equal(24, day.Hours.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), day.Hours[0]);
        Assert.Equal(TimeSpan.FromMinutes(45), day.Hours[14]);
        Assert.Equal(TimeSpan.FromMinutes(45), day.Hours[15]);
        Assert.Equal(TimeSpan.FromHours(1), day.Hours[22]);
        Assert.Equal(TimeSpan.Zero, day.Hours[23]);
        Assert.Equal([2L, 1L], day.Games.Select(g => g.Game.UniverseId));
        Assert.Equal(TimeSpan.FromHours(2), day.Games[1].Total);
        Assert.Equal(new DateTime(2025, 10, 5).ToUniversalTime(), day.FirstStartUtc);
        Assert.Equal(new DateTime(2025, 10, 5, 23, 0, 0).ToUniversalTime(), day.LastEndUtc);
        Assert.Equal(TimeSpan.FromMinutes(30) / 7, day.PreviousWeekAverage); // 23 h 30 → minuit le 4

        var game = stats.GetDayStats(new DateTime(2025, 10, 5), universeId: 1);
        Assert.Equal(TimeSpan.FromHours(2), game.Total);
        Assert.Equal(2, game.SessionCount);

        var empty = stats.GetDayStats(new DateTime(2025, 10, 7));
        Assert.Equal(TimeSpan.Zero, empty.Total);
        Assert.Empty(empty.Games);
        Assert.Null(empty.FirstStartUtc);
    }
}
