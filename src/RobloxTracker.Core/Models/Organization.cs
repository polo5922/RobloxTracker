using System.ComponentModel.DataAnnotations;

namespace RobloxTracker.Core.Models;

/// <summary>Catégorie unique d'un jeu (ex. « Simulateur », « Horreur »).</summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#3B82F6";

    public List<Game> Games { get; set; } = [];

    public override string ToString() => Name;
}

/// <summary>Groupe libre de jeux ; un jeu peut appartenir à plusieurs groupes.</summary>
public class GameGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public List<Game> Games { get; set; } = [];

    public override string ToString() => Name;
}

/// <summary>Position de lecture d'un fichier log Roblox, pour reprendre après un redémarrage.</summary>
public class LogFileState
{
    [Key]
    public string Path { get; set; } = "";
    public long Position { get; set; }
    public DateTime? LastTimestampUtc { get; set; }
    public int? ActiveSessionId { get; set; }
}
