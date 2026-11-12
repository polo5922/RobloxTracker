namespace RobloxTracker.Core.Services;

/// <summary>Liens web Roblox construits à partir d'une session.</summary>
public static class RobloxLinks
{
    /// <summary>Page du jeu sur roblox.com.</summary>
    public static string Game(SessionInfo session) =>
        $"https://www.roblox.com/games/{(session.RootPlaceId is > 0 ? session.RootPlaceId : session.PlaceId)}";

    /// <summary>
    /// Lien qui lance Roblox directement sur le serveur de la session, ou <c>null</c> si le serveur
    /// n'est pas connu (sessions importées, dont le JobId n'est pas un identifiant de serveur).
    /// </summary>
    public static string? Join(SessionInfo session) =>
        Guid.TryParse(session.JobId, out var jobId)
            ? $"https://www.roblox.com/games/start?placeId={session.PlaceId}&gameInstanceId={jobId:D}"
            : null;
}
