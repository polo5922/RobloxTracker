namespace RobloxTracker.Core.Models;

/// <summary>Une partie jouée, du « Joining game » jusqu'à la déconnexion.</summary>
public class PlaySession
{
    public int Id { get; set; }
    public long PlaceId { get; set; }
    public string JobId { get; set; } = "";

    /// <summary>Null tant que l'univers n'a pas été résolu (via le log ou l'API).</summary>
    public long? UniverseId { get; set; }
    public Game? Game { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }

    public bool IsActive => EndUtc is null;

    public TimeSpan Duration => (EndUtc ?? DateTime.UtcNow) - StartUtc;
}
