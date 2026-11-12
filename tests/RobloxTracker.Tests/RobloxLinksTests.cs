using RobloxTracker.Core.Services;

namespace RobloxTracker.Tests;

public sealed class RobloxLinksTests
{
    private static SessionInfo Session(string? jobId, long? rootPlaceId = null) =>
        new(1, 10, 123, "Jeu", null, DateTime.UtcNow, null, JobId: jobId, RootPlaceId: rootPlaceId);

    [Fact]
    public void Join_link_targets_the_same_server()
    {
        var job = Guid.NewGuid();
        Assert.Equal($"https://www.roblox.com/games/start?placeId=123&gameInstanceId={job}", RobloxLinks.Join(Session(job.ToString())));
    }

    [Fact]
    public void No_join_link_without_a_server_id()
    {
        Assert.Null(RobloxLinks.Join(Session("import:abc")));
        Assert.Null(RobloxLinks.Join(Session(null)));
    }

    [Fact]
    public void Game_link_prefers_the_root_place()
    {
        Assert.Equal("https://www.roblox.com/games/999", RobloxLinks.Game(Session(null, 999)));
        Assert.Equal("https://www.roblox.com/games/123", RobloxLinks.Game(Session(null)));
    }
}
