using Microsoft.EntityFrameworkCore;
using RobloxTracker.Core.Data;
using RobloxTracker.Core.Models;

namespace RobloxTracker.Core.Services;

public sealed record SessionInfo(
    int Id, long? UniverseId, long PlaceId, string GameName, string? IconPath, DateTime StartUtc, DateTime? EndUtc,
    string? ThumbnailPath = null, string? JobId = null, long? RootPlaceId = null, string? IconUrl = null)
{
    public bool IsActive => EndUtc is null;
    public TimeSpan Duration => (EndUtc ?? DateTime.UtcNow) - StartUtc;
}

public sealed record UpdateInfo(
    int Id, long UniverseId, string GameName, string? IconPath, bool IsFavorite,
    DateTime UpdatedOnRoblox, DateTime DetectedUtc, string Summary, bool IsNewSinceLastPlay);

public sealed record CategoryInfo(int Id, string Name, string ColorHex, int GameCount);

public sealed record GroupInfo(int Id, string Name, string? Description, int GameCount, List<string?> IconPaths);

/// <summary>Lectures et modifications de la bibliothèque (favoris, catégories, groupes, sessions…).</summary>
public sealed class LibraryService(ITrackerDbFactory dbFactory)
{
    // ----- Sessions -----

    public List<SessionInfo> GetSessions(DateTime? sinceUtc = null, long? universeId = null, int? limit = null)
    {
        using var db = dbFactory.Create();
        var query = db.Sessions.AsNoTracking();
        if (sinceUtc is { } since) query = query.Where(s => s.EndUtc == null || s.EndUtc > since);
        if (universeId is { } id) query = query.Where(s => s.UniverseId == id);
        query = query.OrderByDescending(s => s.StartUtc);
        if (limit is { } n) query = query.Take(n);

        return query.Select(s => new SessionInfo(
                s.Id, s.UniverseId, s.PlaceId,
                s.Game != null ? s.Game.Name : "Place " + s.PlaceId,
                s.Game != null ? s.Game.IconPath : null,
                s.StartUtc, s.EndUtc,
                s.Game != null ? s.Game.ThumbnailPath : null,
                s.JobId,
                s.Game != null ? (long?)s.Game.RootPlaceId : null,
                s.Game != null ? s.Game.IconUrl : null))
            .ToList();
    }

    public SessionInfo? GetActiveSession() =>
        GetSessions(limit: 5).FirstOrDefault(s => s.IsActive);

    public void DeleteSession(int id)
    {
        using var db = dbFactory.Create();
        db.Sessions.Where(s => s.Id == id).ExecuteDelete();
    }

    // ----- Mises à jour -----

    public List<UpdateInfo> GetUpdates(int limit = 300)
    {
        using var db = dbFactory.Create();
        var lastPlayed = db.Sessions.AsNoTracking()
            .Where(s => s.UniverseId != null)
            .GroupBy(s => s.UniverseId!.Value)
            .Select(g => new { Id = g.Key, Last = g.Max(s => s.StartUtc) })
            .ToDictionary(x => x.Id, x => x.Last);

        return db.Updates.AsNoTracking()
            .OrderByDescending(u => u.UpdatedOnRoblox)
            .Take(limit)
            .Select(u => new { u, u.Game!.Name, u.Game.IconPath, u.Game.IsFavorite })
            .AsEnumerable()
            .Select(x => new UpdateInfo(
                x.u.Id, x.u.UniverseId, x.Name, x.IconPath, x.IsFavorite,
                x.u.UpdatedOnRoblox, x.u.DetectedUtc, x.u.Summary,
                lastPlayed.TryGetValue(x.u.UniverseId, out var last) && x.u.UpdatedOnRoblox > last))
            .ToList();
    }

    public List<GameUpdate> GetUpdatesFor(long universeId)
    {
        using var db = dbFactory.Create();
        return db.Updates.AsNoTracking()
            .Where(u => u.UniverseId == universeId)
            .OrderByDescending(u => u.UpdatedOnRoblox)
            .ToList();
    }

    // ----- Jeux -----

    public Game? GetGame(long universeId)
    {
        using var db = dbFactory.Create();
        return db.Games.AsNoTracking()
            .Include(g => g.Category)
            .Include(g => g.Groups)
            .FirstOrDefault(g => g.UniverseId == universeId);
    }

    public void SetFavorite(long universeId, bool isFavorite) =>
        UpdateGame(universeId, g => g.IsFavorite = isFavorite);

    public void SetCategory(long universeId, int? categoryId) =>
        UpdateGame(universeId, g => g.CategoryId = categoryId);

    public void SetNotes(long universeId, string? notes) =>
        UpdateGame(universeId, g => g.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());

    public void SetGroupMembership(long universeId, int groupId, bool isMember)
    {
        using var db = dbFactory.Create();
        var game = db.Games.Include(g => g.Groups).FirstOrDefault(g => g.UniverseId == universeId);
        var group = db.Groups.Find(groupId);
        if (game is null || group is null) return;

        var present = game.Groups.Any(g => g.Id == groupId);
        if (isMember && !present) game.Groups.Add(group);
        else if (!isMember && present) game.Groups.Remove(group);
        db.SaveChanges();
    }

    public void DeleteGame(long universeId)
    {
        using var db = dbFactory.Create();
        var game = db.Games.Find(universeId);
        if (game is null) return;
        db.Games.Remove(game);
        db.SaveChanges();
    }

    private void UpdateGame(long universeId, Action<Game> change)
    {
        using var db = dbFactory.Create();
        var game = db.Games.Find(universeId);
        if (game is null) return;
        change(game);
        db.SaveChanges();
    }

    // ----- Catégories -----

    public List<CategoryInfo> GetCategories()
    {
        using var db = dbFactory.Create();
        return db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryInfo(c.Id, c.Name, c.ColorHex, c.Games.Count))
            .ToList();
    }

    public void AddCategory(string name, string colorHex)
    {
        using var db = dbFactory.Create();
        name = name.Trim();
        if (name.Length == 0 || db.Categories.Any(c => c.Name == name)) return;
        db.Categories.Add(new Category { Name = name, ColorHex = colorHex });
        db.SaveChanges();
    }

    public void UpdateCategory(int id, string name, string colorHex)
    {
        using var db = dbFactory.Create();
        var category = db.Categories.Find(id);
        name = name.Trim();
        if (category is null || name.Length == 0 || db.Categories.Any(c => c.Name == name && c.Id != id)) return;
        category.Name = name;
        category.ColorHex = colorHex;
        db.SaveChanges();
    }

    public void DeleteCategory(int id)
    {
        using var db = dbFactory.Create();
        db.Games.Where(g => g.CategoryId == id).ExecuteUpdate(s => s.SetProperty(g => g.CategoryId, (int?)null));
        db.Categories.Where(c => c.Id == id).ExecuteDelete();
    }

    // ----- Groupes -----

    public List<GroupInfo> GetGroups()
    {
        using var db = dbFactory.Create();
        return db.Groups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupInfo(g.Id, g.Name, g.Description, g.Games.Count,
                g.Games.OrderBy(x => x.Name).Select(x => x.IconPath).Take(8).ToList()))
            .ToList();
    }

    public void AddGroup(string name, string? description)
    {
        using var db = dbFactory.Create();
        name = name.Trim();
        if (name.Length == 0 || db.Groups.Any(g => g.Name == name)) return;
        db.Groups.Add(new GameGroup { Name = name, Description = description?.Trim() });
        db.SaveChanges();
    }

    public void UpdateGroup(int id, string name, string? description)
    {
        using var db = dbFactory.Create();
        var group = db.Groups.Find(id);
        name = name.Trim();
        if (group is null || name.Length == 0 || db.Groups.Any(g => g.Name == name && g.Id != id)) return;
        group.Name = name;
        group.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        db.SaveChanges();
    }

    public void DeleteGroup(int id)
    {
        using var db = dbFactory.Create();
        var group = db.Groups.Include(g => g.Games).FirstOrDefault(g => g.Id == id);
        if (group is null) return;
        db.Groups.Remove(group);
        db.SaveChanges();
    }
}
