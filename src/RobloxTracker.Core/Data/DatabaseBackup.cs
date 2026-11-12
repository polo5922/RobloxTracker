using System.Globalization;
using Microsoft.Data.Sqlite;

namespace RobloxTracker.Core.Data;

/// <summary>
/// Copies de sécurité de la base : une au premier démarrage de chaque version (donc avant toute
/// mise à jour du schéma) et au plus une par jour sinon. Les plus anciennes sont supprimées.
/// </summary>
public static class DatabaseBackup
{
    public const int DefaultKeep = 15;
    private const string Prefix = "tracker-";
    private const string StampFormat = "yyyyMMdd-HHmmss";

    /// <summary>Sauvegarde si la version a changé depuis la dernière copie ou si celle-ci date de plus d'un jour.</summary>
    public static string? CreateIfNeeded(string dbPath, string backupDir, string version, int keep = DefaultKeep)
    {
        if (!File.Exists(dbPath) || !HasData(dbPath)) return null;

        var latest = List(backupDir).FirstOrDefault();
        if (latest is not null && latest.Version == version && DateTime.Now - latest.Date < TimeSpan.FromDays(1))
            return null;

        return Create(dbPath, backupDir, version, keep);
    }

    /// <summary>Copie cohérente (même base en cours d'écriture, WAL compris) via <c>VACUUM INTO</c>.</summary>
    public static string Create(string dbPath, string backupDir, string version, int keep = DefaultKeep)
    {
        Directory.CreateDirectory(backupDir);
        var path = Path.Combine(backupDir, $"{Prefix}{DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture)}-v{version}.db");
        if (File.Exists(path)) File.Delete(path);

        using (var connection = Open(dbPath, SqliteOpenMode.ReadWrite))
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "VACUUM INTO $path";
            cmd.Parameters.AddWithValue("$path", path);
            cmd.ExecuteNonQuery();
        }

        foreach (var old in List(backupDir).Skip(keep))
        {
            try { File.Delete(old.Path); } catch (IOException) { }
        }
        return path;
    }

    /// <summary>
    /// Si la base a disparu ou est vide alors qu'une sauvegarde contient des données, la restaure.
    /// Retourne le chemin de la sauvegarde restaurée.
    /// </summary>
    public static string? RestoreIfMissing(string dbPath, string backupDir)
    {
        if (File.Exists(dbPath) && HasData(dbPath)) return null;

        var backup = List(backupDir).FirstOrDefault(b => HasData(b.Path));
        if (backup is null) return null;

        if (File.Exists(dbPath))
        {
            // On garde la base vide de côté plutôt que de l'écraser.
            SqliteConnection.ClearAllPools();
            var aside = dbPath + ".vide-" + DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture);
            File.Move(dbPath, aside);
            foreach (var suffix in new[] { "-wal", "-shm" })
                if (File.Exists(dbPath + suffix)) File.Move(dbPath + suffix, aside + suffix);
        }
        File.Copy(backup.Path, dbPath);
        return backup.Path;
    }

    /// <summary>Intègre le journal WAL au fichier principal (à appeler à la fermeture).</summary>
    public static void Checkpoint(string dbPath)
    {
        if (!File.Exists(dbPath)) return;
        SqliteConnection.ClearAllPools();
        using var connection = Open(dbPath, SqliteOpenMode.ReadWrite);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        cmd.ExecuteNonQuery();
    }

    public sealed record BackupFile(string Path, DateTime Date, string Version);

    /// <summary>Sauvegardes existantes, de la plus récente à la plus ancienne.</summary>
    public static List<BackupFile> List(string backupDir)
    {
        if (!Directory.Exists(backupDir)) return [];
        return Directory.GetFiles(backupDir, Prefix + "*.db")
            .Select(Parse)
            .OfType<BackupFile>()
            .OrderByDescending(b => b.Date)
            .ThenByDescending(b => File.GetLastWriteTimeUtc(b.Path))
            .ToList();
    }

    private static BackupFile? Parse(string path)
    {
        // tracker-20261101-192600-v1.26.30519.db
        var name = Path.GetFileNameWithoutExtension(path)[Prefix.Length..];
        var versionIndex = name.IndexOf("-v", StringComparison.Ordinal);
        var stamp = versionIndex < 0 ? name : name[..versionIndex];
        if (!DateTime.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        return new BackupFile(path, date, versionIndex < 0 ? "" : name[(versionIndex + 2)..]);
    }

    private static bool HasData(string dbPath)
    {
        try
        {
            using var connection = Open(dbPath, SqliteOpenMode.ReadWrite);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM Sessions) + (SELECT COUNT(*) FROM Games)";
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static SqliteConnection Open(string dbPath, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = mode,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }
}
