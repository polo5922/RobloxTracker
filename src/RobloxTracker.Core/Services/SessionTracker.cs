using System.Diagnostics;
using System.Text;
using RobloxTracker.Core.Data;
using RobloxTracker.Core.Models;

namespace RobloxTracker.Core.Services;

/// <summary>
/// Suit les logs du client Roblox et en déduit les sessions de jeu.
/// Les horodatages viennent du log lui-même et la position de lecture est persistée,
/// donc un redémarrage de l'app reprend exactement là où elle s'était arrêtée.
/// </summary>
public sealed class SessionTracker(ITrackerDbFactory dbFactory, string logsDir, Func<bool> isRobloxRunning)
{
    private readonly SemaphoreSlim _wake = new(0, 1);
    private bool _scannedOnce;

    /// <summary>Filet de sécurité : le dossier est surveillé, mais Windows peut retarder les notifications d'un fichier ouvert.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Attente après une notification pour regrouper les rafales d'écritures du client.</summary>
    public TimeSpan WakeDebounce { get; init; } = TimeSpan.FromMilliseconds(50);

    public Exception? LastError { get; private set; }

    /// <summary>Levé (sur un thread de fond) quand des sessions ont été créées ou modifiées.</summary>
    public event EventHandler? DataChanged;

    public static bool IsRobloxPlayerRunning()
    {
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    /// <summary>Déclenche un scan immédiat (appelé par la surveillance du dossier des logs).</summary>
    public void WakeUp()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { } // un réveil est déjà en attente
    }

    public async Task RunAsync(CancellationToken ct)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                watcher ??= CreateWatcher();

                try
                {
                    if (ScanOnce()) DataChanged?.Invoke(this, EventArgs.Empty);
                    LastError = null;
                }
                catch (Exception ex)
                {
                    LastError = ex;
                }

                try
                {
                    if (await _wake.WaitAsync(PollInterval, ct)) await Task.Delay(WakeDebounce, ct);
                }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            watcher?.Dispose();
        }
    }

    private FileSystemWatcher? CreateWatcher()
    {
        if (!Directory.Exists(logsDir)) return null;
        try
        {
            var watcher = new FileSystemWatcher(logsDir, "*.log")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Changed += (_, _) => WakeUp();
            watcher.Created += (_, _) => WakeUp();
            watcher.Renamed += (_, _) => WakeUp();
            watcher.Error += (_, _) => WakeUp();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null; // on se contente du polling
        }
    }

    /// <summary>Lit les nouvelles lignes de tous les logs. Retourne true si la base a changé.</summary>
    public bool ScanOnce()
    {
        if (!Directory.Exists(logsDir)) return false;

        var files = new DirectoryInfo(logsDir).GetFiles("*.log")
            .Where(f => f.Name.Contains("_Player_", StringComparison.OrdinalIgnoreCase)
                        && !f.Name.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();

        using var db = dbFactory.Create();
        var states = db.LogFiles.ToDictionary(s => s.Path, StringComparer.OrdinalIgnoreCase);
        var running = isRobloxRunning();
        var newest = files.LastOrDefault();

        // Premier lancement : on n'importe pas l'historique, sauf la partie en cours.
        var firstRun = !_scannedOnce && states.Count == 0;
        var changed = false;

        // Au premier passage, regroupe aussi les sessions enregistrées avant l'existence de la fusion.
        if (!_scannedOnce) changed |= SessionMerger.MergeAll(db) > 0;
        _scannedOnce = true;

        foreach (var file in files)
        {
            if (!states.TryGetValue(file.FullName, out var state))
            {
                state = new LogFileState { Path = file.FullName };
                if (firstRun && !(running && file == newest)) state.Position = file.Length;
                db.LogFiles.Add(state);
                states[file.FullName] = state;
            }

            if (file.Length < state.Position) state.Position = 0; // fichier réécrit
            if (file.Length > state.Position) changed |= ReadNewLines(db, file, state);

            var isLive = running && file == newest;
            if (!isLive && state.ActiveSessionId is not null)
            {
                CloseActive(db, state, state.LastTimestampUtc ?? file.LastWriteTimeUtc);
                changed = true;
            }
        }

        db.SaveChanges();
        return changed;
    }

    private static bool ReadNewLines(TrackerDbContext db, FileInfo file, LogFileState state)
    {
        byte[] buffer;
        using (var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            fs.Seek(state.Position, SeekOrigin.Begin);
            buffer = new byte[fs.Length - state.Position];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = fs.Read(buffer, read, buffer.Length - read);
                if (n == 0) break;
                read += n;
            }
            if (read < buffer.Length) Array.Resize(ref buffer, read);
        }

        // On ne traite que les lignes complètes ; la fin sera relue au prochain passage.
        var lastNewLine = Array.LastIndexOf(buffer, (byte)'\n');
        if (lastNewLine < 0) return false;

        var text = Encoding.UTF8.GetString(buffer, 0, lastNewLine + 1);
        state.Position += lastNewLine + 1;

        var changed = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (RobloxLogParser.TryParseTimestamp(line, out var ts)) state.LastTimestampUtc = ts;
            changed |= Apply(db, state, RobloxLogParser.ParseLine(line));
        }
        return changed;
    }

    private static bool Apply(TrackerDbContext db, LogFileState state, LogEvent? evt)
    {
        switch (evt)
        {
            case JoinedEvent join:
            {
                CloseActive(db, state, join.TimestampUtc);
                db.SaveChanges(); // la recherche de la session précédente interroge la base

                var session = db.Sessions.FirstOrDefault(s =>
                    s.JobId == join.JobId && s.PlaceId == join.PlaceId && s.StartUtc == join.TimestampUtc);
                if (session is null)
                {
                    var universeId = db.Sessions
                        .Where(s => s.PlaceId == join.PlaceId && s.UniverseId != null)
                        .Select(s => s.UniverseId)
                        .FirstOrDefault();

                    // Même jeu repris moins d'une minute après : on prolonge l'entrée précédente.
                    session = SessionMerger.FindPrevious(db, join.TimestampUtc, join.PlaceId, universeId);
                    if (session is null)
                    {
                        session = new PlaySession
                        {
                            PlaceId = join.PlaceId,
                            JobId = join.JobId,
                            StartUtc = join.TimestampUtc,
                            UniverseId = universeId,
                        };
                        db.Sessions.Add(session);
                    }
                }
                session.EndUtc = null;

                db.SaveChanges();
                state.ActiveSessionId = session.Id;
                return true;
            }

            case UniverseEvent universe when state.ActiveSessionId is int id:
            {
                var session = db.Sessions.Find(id);
                if (session is null || session.PlaceId != universe.PlaceId || session.UniverseId is not null)
                    return false;

                EnsureGame(db, universe.UniverseId, universe.PlaceId);
                session.UniverseId = universe.UniverseId;

                // Téléportation vers une autre place du même univers : on rattache à la session précédente.
                var previous = SessionMerger.FindPrevious(db, session.StartUtc, session.PlaceId, session.UniverseId, session.Id);
                if (previous is not null)
                {
                    SessionMerger.Absorb(db, previous, session);
                    state.ActiveSessionId = previous.Id;
                    db.SaveChanges();
                }
                return true;
            }

            case LeftEvent left when state.ActiveSessionId is not null:
                CloseActive(db, state, left.TimestampUtc);
                return true;

            default:
                return false;
        }
    }

    private static void CloseActive(TrackerDbContext db, LogFileState state, DateTime endUtc)
    {
        if (state.ActiveSessionId is not int id) return;
        state.ActiveSessionId = null;

        var session = db.Sessions.Find(id);
        if (session is null || session.EndUtc is not null) return;
        session.EndUtc = endUtc < session.StartUtc ? session.StartUtc : endUtc;
    }

    /// <summary>Crée une fiche minimale ; les métadonnées seront complétées par <see cref="GameCatalogService"/>.</summary>
    internal static Game EnsureGame(TrackerDbContext db, long universeId, long placeId)
    {
        var game = db.Games.Find(universeId);
        if (game is not null) return game;

        game = new Game { UniverseId = universeId, RootPlaceId = placeId, Name = $"Expérience {placeId}" };
        db.Games.Add(game);
        return game;
    }
}
