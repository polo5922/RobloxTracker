using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RobloxTracker.Core.Models;

namespace RobloxTracker.Core.Data;

public class TrackerDbContext(DbContextOptions<TrackerDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<PlaySession> Sessions => Set<PlaySession>();
    public DbSet<GameUpdate> Updates => Set<GameUpdate>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<GameGroup> Groups => Set<GameGroup>();
    public DbSet<LogFileState> LogFiles => Set<LogFileState>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Game>()
            .HasMany(g => g.Groups)
            .WithMany(g => g.Games)
            .UsingEntity(j => j.ToTable("GameGroupMembers"));

        b.Entity<Game>()
            .HasOne(g => g.Category)
            .WithMany(c => c.Games)
            .HasForeignKey(g => g.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<PlaySession>()
            .HasOne(s => s.Game)
            .WithMany(g => g.Sessions)
            .HasForeignKey(s => s.UniverseId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<PlaySession>().HasIndex(s => s.StartUtc);
        b.Entity<PlaySession>().HasIndex(s => s.JobId);

        b.Entity<GameUpdate>()
            .HasOne(u => u.Game)
            .WithMany(g => g.Updates)
            .HasForeignKey(u => u.UniverseId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<GameUpdate>().HasIndex(u => new { u.UniverseId, u.UpdatedOnRoblox });

        b.Entity<Category>().HasIndex(c => c.Name).IsUnique();
        b.Entity<GameGroup>().HasIndex(g => g.Name).IsUnique();

        // SQLite ne conserve pas DateTime.Kind : tout est stocké et relu en UTC.
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? v.Value.ToUniversalTime() : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var prop in entity.GetProperties())
        {
            if (prop.ClrType == typeof(DateTime)) prop.SetValueConverter(utc);
            else if (prop.ClrType == typeof(DateTime?)) prop.SetValueConverter(utcNullable);
        }
    }
}

public interface ITrackerDbFactory
{
    TrackerDbContext Create();
}

public sealed class SqliteTrackerDbFactory : ITrackerDbFactory
{
    private readonly DbContextOptions<TrackerDbContext> _options;

    public SqliteTrackerDbFactory(string databasePath)
        : this(new DbContextOptionsBuilder<TrackerDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString())
            .Options)
    {
    }

    public SqliteTrackerDbFactory(DbContextOptions<TrackerDbContext> options) => _options = options;

    public TrackerDbContext Create() => new(_options);

    /// <summary>
    /// Crée la base si besoin, puis ajoute les tables, colonnes et index apparus dans une nouvelle version
    /// (<c>EnsureCreated</c> seul ne touche pas une base existante). Rien n'est jamais supprimé.
    /// </summary>
    public void EnsureDatabase()
    {
        using var db = Create();
        if (!db.Database.EnsureCreated()) UpgradeSchema(db);

        // EF crée la base en mode WAL : les écritures restaient alors dans tracker.db-wal, parfois plusieurs jours,
        // et étaient perdues avec ce fichier. En mode classique, chaque écriture va directement dans tracker.db.
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode = DELETE");
    }

    private static void UpgradeSchema(TrackerDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        db.Database.OpenConnection();
        try
        {
            var tables = Query(connection, "SELECT name FROM sqlite_master WHERE type = 'table'", r => r.GetString(0))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Le script de création complet sert de référence : on n'en exécute que ce qui manque.
            foreach (var statement in db.Database.GenerateCreateScript().Split(";", StringSplitOptions.TrimEntries))
            {
                if (statement.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
                {
                    var name = statement.Split('"')[1];
                    if (!tables.Contains(name)) Execute(connection, statement);
                }
                else if (statement.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase) ||
                         statement.StartsWith("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase))
                {
                    // Un index unique peut échouer sur des données existantes : il n'est pas indispensable.
                    try { Execute(connection, statement.Replace(" INDEX ", " INDEX IF NOT EXISTS ")); }
                    catch (SqliteException) { }
                }
            }

            foreach (var entity in db.Model.GetEntityTypes())
            {
                var table = entity.GetTableName();
                if (table is null) continue;
                var columns = Query(connection, $"PRAGMA table_info(\"{table}\")", r => r.GetString(1))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var store = StoreObjectIdentifier.Table(table, entity.GetSchema());

                foreach (var prop in entity.GetProperties())
                {
                    var column = prop.GetColumnName(store);
                    if (column is null || columns.Contains(column)) continue;

                    var type = prop.GetColumnType();
                    var sql = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {type}";
                    if (!prop.IsNullable) sql += " NOT NULL DEFAULT " + DefaultFor(prop.ClrType, type);
                    Execute(connection, sql);
                    columns.Add(column);
                }
            }
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }

    private static string DefaultFor(Type clrType, string columnType) => columnType.ToUpperInvariant() switch
    {
        _ when clrType == typeof(DateTime) => "'0001-01-01 00:00:00'",
        "INTEGER" or "REAL" => "0",
        "BLOB" => "X''",
        _ => "''",
    };

    private static void Execute(System.Data.Common.DbConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static List<T> Query<T>(System.Data.Common.DbConnection connection, string sql, Func<System.Data.Common.DbDataReader, T> read)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var result = new List<T>();
        while (reader.Read()) result.Add(read(reader));
        return result;
    }
}
