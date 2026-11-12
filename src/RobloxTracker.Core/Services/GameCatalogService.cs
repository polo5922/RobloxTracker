using Microsoft.EntityFrameworkCore;
using RobloxTracker.Core.Data;
using RobloxTracker.Core.Models;

namespace RobloxTracker.Core.Services;

/// <summary>
/// Complète les fiches de jeux à partir de l'API Roblox (nom, créateur, images…)
/// et enregistre une <see cref="GameUpdate"/> quand la date de mise à jour d'un jeu change.
/// </summary>
public sealed class GameCatalogService(ITrackerDbFactory dbFactory, RobloxApiClient api, string imagesDir)
{
    private const int BatchSize = 50;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event EventHandler? DataChanged;

    public Exception? LastError { get; private set; }

    /// <summary>Résout l'univers des sessions orphelines et remplit les nouvelles fiches.</summary>
    public async Task ResolvePendingAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var changed = false;
            using (var db = dbFactory.Create())
            {
                var places = await db.Sessions.Where(s => s.UniverseId == null)
                    .Select(s => s.PlaceId).Distinct().ToListAsync(ct);

                foreach (var placeId in places)
                {
                    var universeId = await api.GetUniverseIdAsync(placeId, ct);
                    if (universeId is null) continue;

                    SessionTracker.EnsureGame(db, universeId.Value, placeId);
                    await db.Sessions.Where(s => s.PlaceId == placeId && s.UniverseId == null)
                        .ForEachAsync(s => s.UniverseId = universeId, ct);
                    changed = true;
                }
                await db.SaveChangesAsync(ct);

                // L'univers maintenant connu peut révéler des sessions consécutives d'un même jeu.
                if (changed) SessionMerger.MergeAll(db);
            }

            List<long> unrefreshed;
            using (var db = dbFactory.Create())
                unrefreshed = await db.Games.Where(g => g.LastRefreshUtc == null).Select(g => g.UniverseId).ToListAsync(ct);

            if (unrefreshed.Count > 0)
            {
                await RefreshCoreAsync(unrefreshed, forceImages: false, ct);
                changed = true;
            }

            LastError = null;
            if (changed) DataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        List<long> ids;
        using (var db = dbFactory.Create())
            ids = await db.Games.Select(g => g.UniverseId).ToListAsync(ct);
        await RefreshAsync(ids, forceImages: false, ct);
    }

    public async Task RefreshAsync(IReadOnlyCollection<long> universeIds, bool forceImages, CancellationToken ct = default)
    {
        if (universeIds.Count == 0) return;
        await _lock.WaitAsync(ct);
        try
        {
            await RefreshCoreAsync(universeIds, forceImages, ct);
            LastError = null;
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RefreshCoreAsync(IReadOnlyCollection<long> universeIds, bool forceImages, CancellationToken ct)
    {
        Directory.CreateDirectory(imagesDir);

        foreach (var chunk in universeIds.Chunk(BatchSize))
        {
            var details = await api.GetGamesAsync(chunk, ct);
            var votes = (await api.GetVotesAsync(chunk, ct)).ToDictionary(v => v.Id);
            var icons = await api.GetIconUrlsAsync(chunk, ct);
            var thumbnails = await api.GetThumbnailUrlsAsync(chunk, ct);

            using var db = dbFactory.Create();
            var games = await db.Games.Where(g => chunk.Contains(g.UniverseId)).ToDictionaryAsync(g => g.UniverseId, ct);

            foreach (var d in details)
            {
                if (!games.TryGetValue(d.Id, out var game)) continue;

                var isNew = game.UpdatedOnRoblox is null;
                var wasUpdated = !isNew && d.Updated > game.UpdatedOnRoblox!.Value.AddSeconds(1);
                var nameChanged = !isNew && game.Name != d.Name;

                if (isNew)
                    AddUpdate(db, game, d.Updated, "Dernière mise à jour connue lors de l'ajout du jeu");
                else if (wasUpdated || nameChanged)
                {
                    var summary = wasUpdated ? "Nouvelle mise à jour publiée" : "Informations modifiées";
                    if (nameChanged) summary += $" — nom : « {game.Name} » → « {d.Name} »";
                    AddUpdate(db, game, d.Updated, summary);
                }

                game.RootPlaceId = d.RootPlaceId;
                game.Name = d.Name;
                game.Description = d.Description;
                game.CreatorName = d.Creator?.Name;
                game.Genre = d.Genre;
                game.Visits = d.Visits;
                game.Playing = d.Playing;
                game.MaxPlayers = d.MaxPlayers;
                game.FavoritedCount = d.FavoritedCount;
                game.CreatedOnRoblox = d.Created;
                game.UpdatedOnRoblox = d.Updated;
                if (votes.TryGetValue(d.Id, out var v))
                {
                    game.UpVotes = v.UpVotes;
                    game.DownVotes = v.DownVotes;
                }

                var refreshImages = forceImages || wasUpdated;
                game.IconPath = await DownloadImageAsync(icons, d.Id, "icon", game.IconPath, refreshImages, ct);
                game.ThumbnailPath = await DownloadImageAsync(thumbnails, d.Id, "thumb", game.ThumbnailPath, refreshImages, ct);
                if (icons.TryGetValue(d.Id, out var iconUrl)) game.IconUrl = iconUrl;
                game.LastRefreshUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private static void AddUpdate(TrackerDbContext db, Game game, DateTime updatedOnRoblox, string summary) =>
        db.Updates.Add(new GameUpdate
        {
            UniverseId = game.UniverseId,
            UpdatedOnRoblox = updatedOnRoblox,
            DetectedUtc = DateTime.UtcNow,
            Summary = summary,
        });

    private async Task<string?> DownloadImageAsync(
        Dictionary<long, string> urls, long universeId, string kind, string? currentPath, bool force, CancellationToken ct)
    {
        if (!urls.TryGetValue(universeId, out var url)) return currentPath;
        if (!force && currentPath is not null && File.Exists(currentPath)) return currentPath;

        var path = Path.Combine(imagesDir, $"{universeId}_{kind}.png");
        try
        {
            await api.DownloadFileAsync(url, path, ct);
            return path;
        }
        catch (HttpRequestException)
        {
            return currentPath;
        }
    }
}
