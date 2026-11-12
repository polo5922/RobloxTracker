using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RobloxTracker.Core.Data;
using RobloxTracker.Core.Models;

namespace RobloxTracker.Core.Services;

public record BackupImportResult(int FilesRead, int Games, int Sessions, TimeSpan TotalTime);

/// <summary>
/// Importe les sauvegardes (.rtb) de l'ancien tracker. Les jours récents contiennent des sessions
/// horodatées ; les plus anciens seulement un total par jeu, converti en une session par jour.
/// Réimporter remplace les données importées précédemment.
/// </summary>
public class BackupImportService(ITrackerDbFactory dbFactory)
{
    public const string JobIdPrefix = "import:";
    private const int MinRemainderSeconds = 60;

    public BackupImportResult ImportFolder(string folder) =>
        Import(Directory.GetFiles(folder, "*.rtb"));

    public BackupImportResult Import(IReadOnlyCollection<string> files)
    {
        // Les sauvegardes sont des instantanés cumulés : on fusionne tous les fichiers en gardant le max par jour et par jeu.
        var totals = new Dictionary<(string Account, DateOnly Day, long Game), double>();
        var sessions = new Dictionary<string, (string Account, long Game, DateTime Start, DateTime End)>();

        foreach (var file in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            ReadBackup(doc.RootElement, totals, sessions);
        }

        var imported = BuildSessions(totals, sessions);

        using var db = dbFactory.Create();

        // À partir de la première session suivie par l'app elle-même, c'est elle qui fait foi.
        var firstNative = db.Sessions.Where(s => !s.JobId.StartsWith(JobIdPrefix))
            .Min(s => (DateTime?)s.StartUtc) ?? DateTime.MaxValue;
        imported = imported.Where(s => s.StartUtc < firstNative).ToList();

        db.Sessions.Where(s => s.JobId.StartsWith(JobIdPrefix)).ExecuteDelete();

        foreach (var universeId in imported.Select(s => s.UniverseId!.Value).Distinct())
        {
            var game = SessionTracker.EnsureGame(db, universeId, 0);
            var first = imported.Where(s => s.UniverseId == universeId).Min(s => s.StartUtc);
            if (first < game.FirstSeenUtc) game.FirstSeenUtc = first;
        }

        db.Sessions.AddRange(imported);
        db.SaveChanges();

        return new BackupImportResult(files.Count, imported.Select(s => s.UniverseId).Distinct().Count(), imported.Count,
            TimeSpan.FromTicks(imported.Sum(s => (s.EndUtc!.Value - s.StartUtc).Ticks)));
    }

    private static void ReadBackup(JsonElement root,
        Dictionary<(string, DateOnly, long), double> totals,
        Dictionary<string, (string, long, DateTime, DateTime)> sessions)
    {
        if (!root.TryGetProperty("gameStorage", out var storage) ||
            !storage.TryGetProperty("allAccountsStorage", out var all)) return;

        foreach (var family in all.EnumerateObject())
        foreach (var account in family.Value.EnumerateObject())
        foreach (var day in account.Value.EnumerateObject())
        {
            if (!DateOnly.TryParseExact(day.Name, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            if (day.Value.TryGetProperty("characters", out var characters))
            {
                foreach (var game in characters.EnumerateObject())
                {
                    if (!long.TryParse(game.Name, out var gameId) ||
                        !game.Value.TryGetProperty("timeOnTheField", out var t) || !t.TryGetDouble(out var seconds)) continue;

                    var key = (account.Name, date, gameId);
                    totals[key] = Math.Max(totals.GetValueOrDefault(key), seconds);
                }
            }

            if (day.Value.TryGetProperty("sessions", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in list.EnumerateArray())
                {
                    if (!s.TryGetProperty("id", out var id) || !s.TryGetProperty("gameId", out var g) ||
                        !long.TryParse(g.ValueKind == JsonValueKind.String ? g.GetString() : g.GetRawText(), out var gameId) ||
                        !s.TryGetProperty("start", out var start) || !start.TryGetInt64(out var startMs)) continue;

                    long endMs = s.TryGetProperty("end", out var end) && end.TryGetInt64(out var e) ? e
                        : s.TryGetProperty("seconds", out var sec) && sec.TryGetDouble(out var d) ? startMs + (long)(d * 1000)
                        : startMs;
                    if (endMs <= startMs) continue;

                    sessions[id.ToString()] = (account.Name, gameId,
                        DateTimeOffset.FromUnixTimeMilliseconds(startMs).UtcDateTime,
                        DateTimeOffset.FromUnixTimeMilliseconds(endMs).UtcDateTime);
                }
            }
        }
    }

    private static List<PlaySession> BuildSessions(
        Dictionary<(string Account, DateOnly Day, long Game), double> totals,
        Dictionary<string, (string Account, long Game, DateTime Start, DateTime End)> sessions)
    {
        var result = new List<PlaySession>();

        // Temps déjà couvert par des sessions réelles, par jour local de début.
        var covered = new Dictionary<(string, DateOnly, long), double>();
        foreach (var (id, s) in sessions)
        {
            result.Add(new PlaySession { JobId = JobIdPrefix + id, UniverseId = s.Game, StartUtc = s.Start, EndUtc = s.End });
            var key = (s.Account, DateOnly.FromDateTime(s.Start.ToLocalTime()), s.Game);
            covered[key] = covered.GetValueOrDefault(key) + (s.End - s.Start).TotalSeconds;
        }

        // Le reste (jours sans sessions détaillées) devient une session par jeu, enchaînées depuis minuit.
        foreach (var dayGroup in totals.GroupBy(t => (t.Key.Account, t.Key.Day)).OrderBy(g => g.Key.Day))
        {
            var midnight = dayGroup.Key.Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
            var dayLength = (midnight.AddDays(1).ToUniversalTime() - midnight.ToUniversalTime()).TotalSeconds;
            double cursor = 0;

            foreach (var (key, total) in dayGroup.OrderByDescending(t => t.Value))
            {
                var seconds = Math.Min(total - covered.GetValueOrDefault(key), dayLength);
                if (seconds < MinRemainderSeconds) continue;

                var offset = Math.Min(cursor, dayLength - seconds);
                cursor += seconds;
                var start = midnight.ToUniversalTime().AddSeconds(offset);
                result.Add(new PlaySession
                {
                    JobId = $"{JobIdPrefix}{key.Account}:{key.Day:yyyy-MM-dd}:{key.Game}",
                    UniverseId = key.Game,
                    StartUtc = start,
                    EndUtc = start.AddSeconds(seconds),
                });
            }
        }

        return result;
    }
}
