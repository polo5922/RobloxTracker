using Microsoft.EntityFrameworkCore;
using RobloxTracker.Core.Data;
using RobloxTracker.Core.Models;

namespace RobloxTracker.Core.Services;

public sealed record GameStats(
    Game Game,
    TimeSpan Total,
    TimeSpan Last7Days,
    TimeSpan Today,
    int SessionCount,
    DateTime? LastPlayedUtc);

public sealed record DayTotal(DateTime Date, TimeSpan Total);

public sealed record PeriodGameTotal(Game Game, TimeSpan Total);

/// <summary>
/// Statistiques d'un mois (barres par jour) ou d'une année (barres par mois).
/// <see cref="TopGames"/> n'est rempli que pour les statistiques globales (tous jeux confondus).
/// </summary>
public sealed record PeriodStats(
    DateTime Start,
    DateTime End,
    IReadOnlyList<DayTotal> Buckets,
    TimeSpan Total,
    int SessionCount,
    int DaysPlayed,
    TimeSpan PreviousTotal,
    IReadOnlyList<PeriodGameTotal> TopGames);

/// <summary>Calcule les temps de jeu. Les jours sont ceux du fuseau local ; une session à cheval sur minuit est répartie.</summary>
public sealed class StatsService(ITrackerDbFactory dbFactory)
{
    private sealed record SessionSpan(long? UniverseId, DateTime StartUtc, DateTime? EndUtc)
    {
        public DateTime EffectiveEndUtc => EndUtc ?? DateTime.UtcNow;
    }

    public List<GameStats> GetGameStats()
    {
        using var db = dbFactory.Create();
        var games = db.Games.AsNoTracking().Include(g => g.Category).Include(g => g.Groups).ToList();
        var sessions = LoadSpans(db).Where(s => s.UniverseId != null).ToLookup(s => s.UniverseId!.Value);

        var (todayStart, now) = (DateTime.Today.ToUniversalTime(), DateTime.UtcNow);
        var weekStart = DateTime.Today.AddDays(-6).ToUniversalTime();

        return games.Select(g =>
        {
            var list = sessions[g.UniverseId].ToList();
            return new GameStats(
                g,
                Sum(list, DateTime.MinValue, DateTime.MaxValue),
                Sum(list, weekStart, now),
                Sum(list, todayStart, now),
                list.Count,
                list.Count == 0 ? null : list.Max(s => s.EffectiveEndUtc));
        }).ToList();
    }

    /// <summary>Temps joué par jour sur les <paramref name="days"/> derniers jours (aujourd'hui inclus).</summary>
    public List<DayTotal> GetDailyTotals(int days)
    {
        using var db = dbFactory.Create();
        var firstDay = DateTime.Today.AddDays(-(days - 1));
        var firstUtc = firstDay.ToUniversalTime();
        var spans = LoadSpans(db).Where(s => s.EffectiveEndUtc > firstUtc).ToList();

        return Enumerable.Range(0, days).Select(i =>
        {
            var day = firstDay.AddDays(i);
            return new DayTotal(day, Sum(spans, day.ToUniversalTime(), day.AddDays(1).ToUniversalTime()));
        }).ToList();
    }

    /// <summary>
    /// Statistiques du mois <paramref name="month"/> de <paramref name="year"/>, ou de toute l'année si <paramref name="month"/> est nul.
    /// Limitées au jeu <paramref name="universeId"/> s'il est renseigné.
    /// </summary>
    public PeriodStats GetPeriodStats(int year, int? month, long? universeId = null, int topCount = 5)
    {
        var start = new DateTime(year, month ?? 1, 1);
        var end = month is null ? start.AddYears(1) : start.AddMonths(1);
        var previousStart = month is null ? start.AddYears(-1) : start.AddMonths(-1);
        var (startUtc, endUtc, previousUtc) = (start.ToUniversalTime(), end.ToUniversalTime(), previousStart.ToUniversalTime());

        using var db = dbFactory.Create();
        var all = LoadSpans(db)
            .Where(s => universeId is null || s.UniverseId == universeId)
            .Where(s => s.StartUtc < endUtc && s.EffectiveEndUtc > previousUtc)
            .ToList();
        var spans = all.Where(s => s.StartUtc < endUtc && s.EffectiveEndUtc > startUtc).ToList();

        var days = new List<DayTotal>();
        for (var day = start; day < end; day = day.AddDays(1))
            days.Add(new DayTotal(day, Sum(spans, day.ToUniversalTime(), day.AddDays(1).ToUniversalTime())));

        var buckets = month is not null
            ? days
            : days.GroupBy(d => d.Date.Month)
                .Select(m => new DayTotal(m.First().Date, m.Aggregate(TimeSpan.Zero, (acc, d) => acc + d.Total)))
                .ToList();

        var top = new List<PeriodGameTotal>();
        if (universeId is null)
        {
            var totals = spans.Where(s => s.UniverseId != null)
                .GroupBy(s => s.UniverseId!.Value)
                .Select(g => (Id: g.Key, Total: Sum(g, startUtc, endUtc)))
                .Where(x => x.Total > TimeSpan.Zero)
                .OrderByDescending(x => x.Total)
                .Take(topCount)
                .ToList();
            var ids = totals.Select(x => x.Id).ToList();
            var games = db.Games.AsNoTracking().Where(g => ids.Contains(g.UniverseId)).ToDictionary(g => g.UniverseId);
            top = totals.Where(x => games.ContainsKey(x.Id)).Select(x => new PeriodGameTotal(games[x.Id], x.Total)).ToList();
        }

        return new PeriodStats(
            start,
            end,
            buckets,
            days.Aggregate(TimeSpan.Zero, (acc, d) => acc + d.Total),
            spans.Count,
            days.Count(d => d.Total > TimeSpan.Zero),
            Sum(all, previousUtc, startUtc),
            top);
    }

    /// <summary>Première année où une session a été enregistrée (année en cours s'il n'y en a aucune).</summary>
    public int GetFirstYear(long? universeId = null)
    {
        using var db = dbFactory.Create();
        var first = db.Sessions.AsNoTracking()
            .Where(s => universeId == null || s.UniverseId == universeId)
            .OrderBy(s => s.StartUtc)
            .Select(s => (DateTime?)s.StartUtc)
            .FirstOrDefault();
        return first is { } utc ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().Year : DateTime.Today.Year;
    }

    public static TimeSpan Overlap(DateTime start, DateTime end, DateTime rangeStart, DateTime rangeEnd)
    {
        var s = start > rangeStart ? start : rangeStart;
        var e = end < rangeEnd ? end : rangeEnd;
        return e > s ? e - s : TimeSpan.Zero;
    }

    private static List<SessionSpan> LoadSpans(TrackerDbContext db) =>
        db.Sessions.AsNoTracking().Select(s => new SessionSpan(s.UniverseId, s.StartUtc, s.EndUtc)).ToList();

    private static TimeSpan Sum(IEnumerable<SessionSpan> spans, DateTime fromUtc, DateTime toUtc) =>
        spans.Aggregate(TimeSpan.Zero, (acc, s) => acc + Overlap(s.StartUtc, s.EffectiveEndUtc, fromUtc, toUtc));
}
