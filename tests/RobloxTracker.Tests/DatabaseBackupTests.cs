using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RobloxTracker.Core.Data;
using RobloxTracker.Core.Models;

namespace RobloxTracker.Tests;

public sealed class DatabaseBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rt-backup-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly string _backups;
    private readonly SqliteTrackerDbFactory _db;

    public DatabaseBackupTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "tracker.db");
        _backups = Path.Combine(_dir, "sauvegardes");
        _db = new SqliteTrackerDbFactory(_dbPath);
        _db.EnsureDatabase();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private void AddGame(long id, string name = "Jeu")
    {
        using var db = _db.Create();
        db.Games.Add(new Game { UniverseId = id, Name = name, Notes = "à garder" });
        db.SaveChanges();
    }

    private int CountGames(string path)
    {
        using var db = new SqliteTrackerDbFactory(path).Create();
        return db.Games.Count();
    }

    [Fact]
    public void Empty_database_is_not_backed_up()
    {
        Assert.Null(DatabaseBackup.CreateIfNeeded(_dbPath, _backups, "1.0.0"));
    }

    [Fact]
    public void Backup_is_made_once_per_version_and_contains_the_data()
    {
        AddGame(1);
        var first = DatabaseBackup.CreateIfNeeded(_dbPath, _backups, "1.0.0");
        Assert.NotNull(first);
        Assert.Equal(1, CountGames(first));

        Assert.Null(DatabaseBackup.CreateIfNeeded(_dbPath, _backups, "1.0.0"));
        Assert.NotNull(DatabaseBackup.CreateIfNeeded(_dbPath, _backups, "1.0.1"));
        Assert.Equal(["1.0.1", "1.0.0"], DatabaseBackup.List(_backups).Select(b => b.Version));
    }

    [Fact]
    public void Old_backups_are_pruned()
    {
        AddGame(1);
        Directory.CreateDirectory(_backups);
        for (var i = 1; i <= 5; i++)
            File.WriteAllText(Path.Combine(_backups, $"tracker-2020010{i}-120000-v0.9.db"), "");

        DatabaseBackup.Create(_dbPath, _backups, "1.0.0", keep: 3);

        var left = DatabaseBackup.List(_backups);
        Assert.Equal(3, left.Count);
        Assert.Equal("1.0.0", left[0].Version);
        Assert.Equal(new DateTime(2020, 1, 4, 12, 0, 0), left[2].Date);
    }

    [Fact]
    public void Missing_or_empty_database_is_restored_from_the_latest_backup()
    {
        AddGame(1);
        AddGame(2);
        DatabaseBackup.Create(_dbPath, _backups, "1.0.0");

        // La base est remplacée par une base vide (ex. recréée après sa perte).
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        _db.EnsureDatabase();
        SqliteConnection.ClearAllPools();

        Assert.NotNull(DatabaseBackup.RestoreIfMissing(_dbPath, _backups));
        Assert.Equal(2, CountGames(_dbPath));
        Assert.Null(DatabaseBackup.RestoreIfMissing(_dbPath, _backups));
    }

    [Fact]
    public void Upgrade_adds_missing_columns_and_tables_without_losing_data()
    {
        AddGame(1, "Mon jeu");
        using (var db = _db.Create())
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Games DROP COLUMN Notes");
            db.Database.ExecuteSqlRaw("DROP TABLE Updates");
        }

        _db.EnsureDatabase();

        using var check = _db.Create();
        var game = check.Games.Single();
        Assert.Equal("Mon jeu", game.Name);
        Assert.Null(game.Notes);
        Assert.Empty(check.Updates);
    }

    [Fact]
    public void Writes_go_straight_to_the_database_file()
    {
        AddGame(1);

        Assert.False(File.Exists(_dbPath + "-wal"));
        using var db = _db.Create();
        Assert.Equal("delete", db.Database.SqlQueryRaw<string>("PRAGMA journal_mode").AsEnumerable().First());
    }

    [Fact]
    public void Checkpoint_moves_the_wal_into_the_database_file()
    {
        AddGame(1);
        DatabaseBackup.Checkpoint(_dbPath);

        var wal = _dbPath + "-wal";
        Assert.True(!File.Exists(wal) || new FileInfo(wal).Length == 0);
        Assert.Equal(1, CountGames(_dbPath));
    }
}
