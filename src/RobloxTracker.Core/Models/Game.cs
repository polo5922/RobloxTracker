using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RobloxTracker.Core.Models;

/// <summary>Une expérience Roblox, identifiée par son univers.</summary>
public class Game
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long UniverseId { get; set; }

    public long RootPlaceId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? CreatorName { get; set; }
    public string? Genre { get; set; }
    public long Visits { get; set; }
    public int Playing { get; set; }
    public int MaxPlayers { get; set; }
    public long FavoritedCount { get; set; }
    public long UpVotes { get; set; }
    public long DownVotes { get; set; }
    public DateTime? CreatedOnRoblox { get; set; }
    public DateTime? UpdatedOnRoblox { get; set; }

    public string? IconPath { get; set; }
    public string? ThumbnailPath { get; set; }
    /// <summary>Adresse de l'icône sur les serveurs Roblox (pour Discord).</summary>
    public string? IconUrl { get; set; }

    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastRefreshUtc { get; set; }

    public bool IsFavorite { get; set; }
    public string? Notes { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public List<GameGroup> Groups { get; set; } = [];
    public List<PlaySession> Sessions { get; set; } = [];
    public List<GameUpdate> Updates { get; set; } = [];

    [NotMapped]
    public double? LikeRatio => UpVotes + DownVotes == 0 ? null : (double)UpVotes / (UpVotes + DownVotes);

    [NotMapped]
    public string WebUrl => $"https://www.roblox.com/games/{RootPlaceId}";
}
