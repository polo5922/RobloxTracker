using System.Net.Http.Json;
using System.Text.Json;

namespace RobloxTracker.Core.Services;

public sealed record RobloxCreator(long Id, string? Name, string? Type);

public sealed record RobloxGameDetails(
    long Id,
    long RootPlaceId,
    string Name,
    string? Description,
    RobloxCreator? Creator,
    long Visits,
    int Playing,
    int MaxPlayers,
    DateTime Created,
    DateTime Updated,
    long FavoritedCount,
    string? Genre);

public sealed record RobloxVotes(long Id, long UpVotes, long DownVotes);

/// <summary>Client des API web publiques de Roblox (aucune authentification requise).</summary>
public sealed class RobloxApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record UniverseResponse(long? UniverseId);
    private sealed record DataResponse<T>(List<T> Data);
    private sealed record ThumbnailEntry(long TargetId, string? State, string? ImageUrl);
    private sealed record MultiThumbnailEntry(long UniverseId, List<ThumbnailEntry>? Thumbnails);

    public async Task<long?> GetUniverseIdAsync(long placeId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", ct);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadFromJsonAsync<UniverseResponse>(Json, ct);
        return body?.UniverseId;
    }

    public async Task<List<RobloxGameDetails>> GetGamesAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct = default)
    {
        var body = await GetDataAsync<RobloxGameDetails>(
            $"https://games.roblox.com/v1/games?universeIds={string.Join(',', universeIds)}", ct);
        return body;
    }

    public Task<List<RobloxVotes>> GetVotesAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct = default) =>
        GetDataAsync<RobloxVotes>($"https://games.roblox.com/v1/games/votes?universeIds={string.Join(',', universeIds)}", ct);

    public async Task<Dictionary<long, string>> GetIconUrlsAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct = default)
    {
        var data = await GetDataAsync<ThumbnailEntry>(
            "https://thumbnails.roblox.com/v1/games/icons?size=512x512&format=Png&isCircular=false&returnPolicy=PlaceHolder" +
            $"&universeIds={string.Join(',', universeIds)}", ct);

        return data
            .Where(t => t.State == "Completed" && !string.IsNullOrEmpty(t.ImageUrl))
            .ToDictionary(t => t.TargetId, t => t.ImageUrl!);
    }

    public async Task<Dictionary<long, string>> GetThumbnailUrlsAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct = default)
    {
        var data = await GetDataAsync<MultiThumbnailEntry>(
            "https://thumbnails.roblox.com/v1/games/multiget/thumbnails?countPerUniverse=1&defaults=true&size=768x432&format=Png&isCircular=false" +
            $"&universeIds={string.Join(',', universeIds)}", ct);

        var result = new Dictionary<long, string>();
        foreach (var entry in data)
        {
            var thumb = entry.Thumbnails?.FirstOrDefault(t => t.State == "Completed" && !string.IsNullOrEmpty(t.ImageUrl));
            if (thumb is not null) result[entry.UniverseId] = thumb.ImageUrl!;
        }
        return result;
    }

    /// <summary>Télécharge une image dans un fichier temporaire puis remplace la cible.</summary>
    public async Task DownloadFileAsync(string url, string destinationPath, CancellationToken ct = default)
    {
        var temp = destinationPath + ".tmp";
        using (var response = await http.GetAsync(url, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(temp);
            await response.Content.CopyToAsync(file, ct);
        }
        File.Move(temp, destinationPath, overwrite: true);
    }

    private async Task<List<T>> GetDataAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DataResponse<T>>(Json, ct);
        return body?.Data ?? [];
    }
}
