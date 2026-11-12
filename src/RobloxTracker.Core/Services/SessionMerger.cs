using RobloxTracker.Core.Data;
using RobloxTracker.Core.Models;

namespace RobloxTracker.Core.Services;

/// <summary>
/// Regroupe en une seule entrée les sessions consécutives d'un même jeu séparées de moins de <see cref="MergeGap"/>
/// (reconnexion, changement de serveur, téléportation interne…). Les sessions importées ne sont jamais touchées.
/// </summary>
public static class SessionMerger
{
    public static readonly TimeSpan MergeGap = TimeSpan.FromMinutes(1);

    private const string ImportPrefix = "import:";

    /// <summary>Même univers quand il est connu des deux côtés, sinon même place.</summary>
    internal static bool SameGame(PlaySession s, long placeId, long? universeId) =>
        s.UniverseId is not null && universeId is not null ? s.UniverseId == universeId : s.PlaceId == placeId;

    /// <summary>
    /// Dernière session terminée avant <paramref name="startUtc"/> si elle est du même jeu
    /// et s'est terminée il y a moins de <see cref="MergeGap"/>.
    /// </summary>
    internal static PlaySession? FindPrevious(TrackerDbContext db, DateTime startUtc, long placeId, long? universeId, int? excludeId = null)
    {
        var minEnd = startUtc - MergeGap;
        var previous = db.Sessions
            .Where(s => s.Id != excludeId && s.EndUtc != null && s.EndUtc > minEnd && s.StartUtc <= startUtc
                        && !s.JobId.StartsWith(ImportPrefix))
            .OrderByDescending(s => s.EndUtc)
            .FirstOrDefault();
        return previous is not null && SameGame(previous, placeId, universeId) ? previous : null;
    }

    /// <summary>Fusionne <paramref name="other"/> dans <paramref name="keep"/> puis la supprime.</summary>
    internal static void Absorb(TrackerDbContext db, PlaySession keep, PlaySession other)
    {
        keep.EndUtc = keep.EndUtc is null || other.EndUtc is null ? null
            : keep.EndUtc > other.EndUtc ? keep.EndUtc : other.EndUtc;
        keep.UniverseId ??= other.UniverseId;

        foreach (var state in db.LogFiles.Where(s => s.ActiveSessionId == other.Id))
            state.ActiveSessionId = keep.Id;
        db.Sessions.Remove(other);
    }

    /// <summary>Fusionne toutes les sessions natives adjacentes. Retourne le nombre de sessions absorbées.</summary>
    public static int MergeAll(TrackerDbContext db)
    {
        var sessions = db.Sessions
            .Where(s => !s.JobId.StartsWith(ImportPrefix))
            .OrderBy(s => s.StartUtc)
            .ToList();

        var merged = 0;
        PlaySession? previous = null;
        foreach (var current in sessions)
        {
            if (previous is { EndUtc: { } end }
                && current.StartUtc >= previous.StartUtc
                && current.StartUtc - end < MergeGap
                && SameGame(previous, current.PlaceId, current.UniverseId))
            {
                Absorb(db, previous, current);
                merged++;
                continue;
            }
            previous = current;
        }

        if (merged > 0) db.SaveChanges();
        return merged;
    }
}
