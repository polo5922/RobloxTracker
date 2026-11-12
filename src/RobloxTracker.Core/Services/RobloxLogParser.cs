using System.Globalization;
using System.Text.RegularExpressions;

namespace RobloxTracker.Core.Services;

public abstract record LogEvent(DateTime TimestampUtc);

/// <summary>Le joueur rejoint un serveur (« ! Joining game '&lt;jobId&gt;' place &lt;placeId&gt; »).</summary>
public sealed record JoinedEvent(DateTime TimestampUtc, long PlaceId, string JobId) : LogEvent(TimestampUtc);

/// <summary>Le log associe une place à son univers (« placeid:… universeid:… »).</summary>
public sealed record UniverseEvent(DateTime TimestampUtc, long PlaceId, long UniverseId) : LogEvent(TimestampUtc);

/// <summary>Le joueur quitte le serveur.</summary>
public sealed record LeftEvent(DateTime TimestampUtc) : LogEvent(TimestampUtc);

/// <summary>
/// Analyse les lignes des logs du client Roblox, de la forme
/// <c>2026-10-30T15:52:56.264Z,3.264522,8b38,6 [FLog::Output] message</c>.
/// </summary>
public static partial class RobloxLogParser
{
    [GeneratedRegex(@"! Joining game '(?<job>[0-9a-fA-F-]+)' place (?<place>\d+)")]
    private static partial Regex JoinRegex();

    // Les deux champs apparaissent sur la même ligne mais pas forcément côte à côte (visitid, userid… entre eux).
    [GeneratedRegex(@"\bplaceid:\s*(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceIdRegex();

    [GeneratedRegex(@"\buniverseid:\s*(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex UniverseIdRegex();

    [GeneratedRegex(@"Time to disconnect replication data|leaveUGCGameInternal|Client:Disconnect|NetworkClient:Remove")]
    private static partial Regex LeaveRegex();

    public static bool TryParseTimestamp(string line, out DateTime timestampUtc)
    {
        timestampUtc = default;
        var comma = line.IndexOf(',');
        if (comma < 19 || comma > 35) return false;

        return DateTime.TryParse(
            line.AsSpan(0, comma),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out timestampUtc);
    }

    /// <summary>Retourne l'événement porté par la ligne, ou null si elle n'en contient pas.</summary>
    public static LogEvent? ParseLine(string line)
    {
        if (!TryParseTimestamp(line, out var ts)) return null;

        var join = JoinRegex().Match(line);
        if (join.Success)
            return new JoinedEvent(ts, long.Parse(join.Groups["place"].ValueSpan), join.Groups["job"].Value);

        var universe = UniverseIdRegex().Match(line);
        var place = PlaceIdRegex().Match(line);
        if (universe.Success && place.Success)
            return new UniverseEvent(ts,
                long.Parse(place.Groups["id"].ValueSpan),
                long.Parse(universe.Groups["id"].ValueSpan));

        if (LeaveRegex().IsMatch(line))
            return new LeftEvent(ts);

        return null;
    }
}
