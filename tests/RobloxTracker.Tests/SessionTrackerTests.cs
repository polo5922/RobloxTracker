using Microsoft.Data.Sqlite;
using RobloxTracker.Core.Data;
using RobloxTracker.Core.Services;

namespace RobloxTracker.Tests;

public sealed class SessionTrackerTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 30, 15, 0, 0, DateTimeKind.Utc);
    private const string Job1 = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string Job2 = "aaaaaaaa-0000-0000-0000-000000000002";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rt-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _logs;
    private readonly SqliteTrackerDbFactory _db;
    private bool _running = true;

    public SessionTrackerTests()
    {
        _logs = Path.Combine(_dir, "logs");
        Directory.CreateDirectory(_logs);
        _db = new SqliteTrackerDbFactory(Path.Combine(_dir, "test.db"));
        _db.EnsureDatabase();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private SessionTracker NewTracker() => new(_db, _logs, () => _running);

    private string LogPath(string name = "0.123_20261030T150000Z_Player_ABC_last.log") => Path.Combine(_logs, name);

    private static string Line(DateTime ts, string message) =>
        $"{ts:yyyy-MM-ddTHH:mm:ss.fff}Z,1.0,8b38,6 [FLog::Output] {message}\n";

    private static string Join(DateTime ts, string job, long place) => Line(ts, $"! Joining game '{job}' place {place} at 1.2.3.4");
    private static string Universe(DateTime ts, long place, long universe) => Line(ts, $"placeid:{place}, visitid:0, universeid:{universe}");
    private static string Leave(DateTime ts) => Line(ts, "Time to disconnect replication data: 0.01");

    private void Append(string text, string? path = null) => File.AppendAllText(path ?? LogPath(), text);

    private List<Core.Models.PlaySession> Sessions()
    {
        using var db = _db.Create();
        return db.Sessions.OrderBy(s => s.StartUtc).ToList();
    }

    [Fact]
    public void Join_then_leave_records_one_closed_session_with_its_game()
    {
        var tracker = NewTracker();
        File.WriteAllText(LogPath(), "");
        tracker.ScanOnce(); // premier passage : le fichier vide est enregistré

        Append(Join(T0, Job1, 100) + Universe(T0.AddSeconds(5), 100, 9000));
        Assert.True(tracker.ScanOnce());

        var active = Assert.Single(Sessions());
        Assert.Null(active.EndUtc);
        Assert.Equal(9000, active.UniverseId);

        Append(Leave(T0.AddMinutes(25)));
        Assert.True(tracker.ScanOnce());

        var session = Assert.Single(Sessions());
        Assert.Equal(T0, session.StartUtc);
        Assert.Equal(T0.AddMinutes(25), session.EndUtc);

        using var db = _db.Create();
        var game = Assert.Single(db.Games.ToList());
        Assert.Equal(9000, game.UniverseId);
        Assert.Equal(100, game.RootPlaceId);
    }

    [Fact]
    public void Join_is_detected_while_Roblox_keeps_the_log_open()
    {
        var tracker = NewTracker();
        using var writer = new FileStream(LogPath(), FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer.Write(System.Text.Encoding.UTF8.GetBytes(Line(T0, "start")));
        writer.Flush();
        tracker.ScanOnce();

        writer.Write(System.Text.Encoding.UTF8.GetBytes(Join(T0.AddSeconds(5), Job1, 100)));
        writer.Flush(); // pas de fermeture : la taille annoncée par le dossier reste périmée
        Assert.True(tracker.ScanOnce());
        Assert.Null(Assert.Single(Sessions()).EndUtc);
    }

    [Fact]
    public void Another_Roblox_launch_does_not_close_the_game_in_progress()
    {
        var tracker = NewTracker();
        using var writer = new FileStream(LogPath(), FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        tracker.ScanOnce();
        writer.Write(System.Text.Encoding.UTF8.GetBytes(Join(T0, Job1, 100)));
        writer.Flush();
        tracker.ScanOnce();

        var other = LogPath("0.123_20261030T160000Z_Player_DEF_last.log");
        Append(Line(T0.AddHours(1), "launcher"), other);
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddMinutes(1));
        tracker.ScanOnce();
        Assert.Null(Assert.Single(Sessions()).EndUtc);
    }

    [Fact]
    public void Old_connection_closing_after_a_rejoin_does_not_end_the_new_session()
    {
        var tracker = NewTracker();
        File.WriteAllText(LogPath(), "");
        tracker.ScanOnce();

        var rejoin = T0.AddMinutes(15);
        Append(Join(T0, Job1, 100) + Line(rejoin, "Client:Disconnect") + Join(rejoin.AddMilliseconds(400), Job1, 100)
               + Line(rejoin.AddMilliseconds(600), "Client:Disconnect") + Line(rejoin.AddMilliseconds(700), "NetworkClient:Remove"));
        tracker.ScanOnce();
        Assert.Null(Assert.Single(Sessions()).EndUtc);
    }

    [Fact]
    public void Leaving_to_the_home_screen_right_after_joining_ends_the_session()
    {
        var tracker = NewTracker();
        File.WriteAllText(LogPath(), "");
        tracker.ScanOnce();

        Append(Join(T0, Job1, 100) + Line(T0.AddSeconds(3), "leaveUGCGameInternal"));
        tracker.ScanOnce();
        Assert.Equal(T0.AddSeconds(3), Assert.Single(Sessions()).EndUtc);
    }

    [Fact]
    public void Joining_another_game_closes_the_previous_session()
    {
        var tracker = NewTracker();
        File.WriteAllText(LogPath(), "");
        tracker.ScanOnce();

        Append(Join(T0, Job1, 100) + Join(T0.AddMinutes(10), Job2, 200));
        tracker.ScanOnce();

        var sessions = Sessions();
        Assert.Equal(2, sessions.Count);
        Assert.Equal(T0.AddMinutes(10), sessions[0].EndUtc);
        Assert.Null(sessions[1].EndUtc);
        Assert.Equal(200, sessions[1].PlaceId);
    }

    [Fact]
    public void Session_is_closed_at_last_log_line_when_Roblox_is_closed()
    {
        var tracker = NewTracker();
        File.WriteAllText(LogPath(), "");
        tracker.ScanOnce();

        Append(Join(T0, Job1, 100) + Line(T0.AddMinutes(42), "still playing"));
        tracker.ScanOnce();
        Assert.Null(Assert.Single(Sessions()).EndUtc);

        _running = false;
        Assert.True(tracker.ScanOnce());
        Assert.Equal(T0.AddMinutes(42), Assert.Single(Sessions()).EndUtc);
    }

    [Fact]
    public void First_run_skips_old_logs_but_keeps_the_live_one()
    {
        var old = LogPath("0.1_20261029T100000Z_Player_OLD_last.log");
        File.WriteAllText(old, Join(T0.AddDays(-1), Job1, 100) + Leave(T0.AddDays(-1).AddHours(1)));
        File.SetLastWriteTimeUtc(old, T0.AddDays(-1));
        File.WriteAllText(LogPath(), Join(T0, Job2, 200));

        NewTracker().ScanOnce();

        var session = Assert.Single(Sessions());
        Assert.Equal(200, session.PlaceId);
        Assert.Null(session.EndUtc);
    }

    [Fact]
    public void Restarting_the_app_does_not_duplicate_sessions()
    {
        File.WriteAllText(LogPath(), "");
        NewTracker().ScanOnce();
        Append(Join(T0, Job1, 100));
        NewTracker().ScanOnce();

        // Nouvelle instance (redémarrage de l'app) : la position de lecture est reprise.
        Append(Leave(T0.AddMinutes(5)));
        NewTracker().ScanOnce();

        var session = Assert.Single(Sessions());
        Assert.Equal(T0.AddMinutes(5), session.EndUtc);
    }

    [Fact]
    public void Incomplete_last_line_is_read_on_next_pass()
    {
        var tracker = NewTracker();
        File.WriteAllText(LogPath(), "");
        tracker.ScanOnce();

        var join = Join(T0, Job1, 100);
        Append(join[..20]);
        Assert.False(tracker.ScanOnce());
        Assert.Empty(Sessions());

        Append(join[20..]);
        Assert.True(tracker.ScanOnce());
        Assert.Single(Sessions());
    }

    private SessionTracker StartedTracker()
    {
        var tracker = NewTracker();
        File.WriteAllText(LogPath(), "");
        tracker.ScanOnce();
        return tracker;
    }

    [Fact]
    public void Rejoining_the_same_game_within_a_minute_extends_the_same_entry()
    {
        var tracker = StartedTracker();
        Append(Join(T0, Job1, 100) + Universe(T0.AddSeconds(5), 100, 9000) + Leave(T0.AddMinutes(10))
               + Join(T0.AddMinutes(10).AddSeconds(30), Job2, 100) + Leave(T0.AddMinutes(20)));
        tracker.ScanOnce();

        var session = Assert.Single(Sessions());
        Assert.Equal(T0, session.StartUtc);
        Assert.Equal(T0.AddMinutes(20), session.EndUtc);
    }

    [Fact]
    public void Rejoining_the_same_game_after_more_than_a_minute_creates_a_new_entry()
    {
        var tracker = StartedTracker();
        Append(Join(T0, Job1, 100) + Leave(T0.AddMinutes(10)) + Join(T0.AddMinutes(12), Job2, 100));
        tracker.ScanOnce();

        Assert.Equal(2, Sessions().Count);
    }

    [Fact]
    public void Another_game_within_a_minute_is_a_new_entry()
    {
        var tracker = StartedTracker();
        Append(Join(T0, Job1, 100) + Leave(T0.AddMinutes(10)) + Join(T0.AddMinutes(10).AddSeconds(20), Job2, 200));
        tracker.ScanOnce();

        Assert.Equal(2, Sessions().Count);
    }

    [Fact]
    public void Server_hop_without_leave_line_stays_one_entry_and_stays_active()
    {
        var tracker = StartedTracker();
        Append(Join(T0, Job1, 100) + Join(T0.AddMinutes(5), Job2, 100));
        tracker.ScanOnce();

        var session = Assert.Single(Sessions());
        Assert.Null(session.EndUtc);

        Append(Leave(T0.AddMinutes(8)));
        tracker.ScanOnce();
        Assert.Equal(T0.AddMinutes(8), Assert.Single(Sessions()).EndUtc);
    }

    [Fact]
    public void Teleport_to_another_place_of_the_same_universe_is_merged()
    {
        var tracker = StartedTracker();
        Append(Join(T0, Job1, 100) + Universe(T0.AddSeconds(5), 100, 9000) + Leave(T0.AddMinutes(10))
               + Join(T0.AddMinutes(10).AddSeconds(10), Job2, 101));
        tracker.ScanOnce();
        Assert.Equal(2, Sessions().Count); // univers de la place 101 encore inconnu

        Append(Universe(T0.AddMinutes(10).AddSeconds(15), 101, 9000) + Leave(T0.AddMinutes(30)));
        tracker.ScanOnce();

        var session = Assert.Single(Sessions());
        Assert.Equal(T0, session.StartUtc);
        Assert.Equal(T0.AddMinutes(30), session.EndUtc);
    }

    [Fact]
    public void Existing_adjacent_sessions_are_merged_but_imports_are_left_alone()
    {
        using (var db = _db.Create())
        {
            db.Games.Add(new() { UniverseId = 10, RootPlaceId = 1, Name = "Jeu" });
            db.Sessions.AddRange(
                new() { PlaceId = 1, UniverseId = 10, JobId = "a", StartUtc = T0, EndUtc = T0.AddMinutes(5) },
                new() { PlaceId = 1, UniverseId = 10, JobId = "b", StartUtc = T0.AddMinutes(5).AddSeconds(40), EndUtc = T0.AddMinutes(9) },
                new() { PlaceId = 1, UniverseId = 10, JobId = "import:1", StartUtc = T0.AddDays(-1), EndUtc = T0.AddDays(-1).AddMinutes(5) },
                new() { PlaceId = 1, UniverseId = 10, JobId = "import:2", StartUtc = T0.AddDays(-1).AddMinutes(5), EndUtc = T0.AddDays(-1).AddMinutes(9) });
            db.SaveChanges();

            Assert.Equal(1, SessionMerger.MergeAll(db));
            Assert.Equal(0, SessionMerger.MergeAll(db));
        }

        var sessions = Sessions();
        Assert.Equal(3, sessions.Count);
        Assert.Equal(T0.AddMinutes(9), sessions.Single(s => s.JobId == "a").EndUtc);
    }

    [Fact]
    public async Task Log_writes_are_picked_up_without_waiting_for_the_poll()
    {
        File.WriteAllText(LogPath(), "");
        var tracker = new SessionTracker(_db, _logs, () => true) { PollInterval = TimeSpan.FromSeconds(30) };
        var changed = new SemaphoreSlim(0);
        tracker.DataChanged += (_, _) => changed.Release();

        using var cts = new CancellationTokenSource();
        var run = tracker.RunAsync(cts.Token);
        await Task.Delay(300); // premier scan + mise en place de la surveillance

        Append(Join(T0, Job1, 100));
        Assert.True(await changed.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Single(Sessions());

        cts.Cancel();
        await run;
    }
}
