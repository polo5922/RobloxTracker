namespace RobloxTracker.Core.Models;

/// <summary>Une mise à jour détectée sur un jeu (date « updated » de l'API Roblox).</summary>
public class GameUpdate
{
    public int Id { get; set; }
    public long UniverseId { get; set; }
    public Game? Game { get; set; }

    public DateTime UpdatedOnRoblox { get; set; }
    public DateTime DetectedUtc { get; set; } = DateTime.UtcNow;
    public string Summary { get; set; } = "";
}
