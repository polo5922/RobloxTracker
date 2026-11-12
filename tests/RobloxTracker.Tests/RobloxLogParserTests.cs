using RobloxTracker.Core.Services;

namespace RobloxTracker.Tests;

public class RobloxLogParserTests
{
    private const string Prefix = "2026-10-30T15:52:56.264Z,3.264522,8b38,6 ";

    [Fact]
    public void Timestamp_is_parsed_as_utc()
    {
        Assert.True(RobloxLogParser.TryParseTimestamp(Prefix + "[FLog::Output] hello", out var ts));
        Assert.Equal(DateTimeKind.Utc, ts.Kind);
        Assert.Equal(new DateTime(2026, 10, 30, 15, 52, 56, 264, DateTimeKind.Utc), ts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("random text without timestamp")]
    [InlineData("    at Something.Stack.Trace()")]
    public void Lines_without_timestamp_are_ignored(string line)
    {
        Assert.False(RobloxLogParser.TryParseTimestamp(line, out _));
        Assert.Null(RobloxLogParser.ParseLine(line));
    }

    [Fact]
    public void Join_line_gives_place_and_job()
    {
        var evt = RobloxLogParser.ParseLine(Prefix +
            "[FLog::Output] ! Joining game 'f3a1c2d4-1111-2222-3333-444455556666' place 920587237 at 128.116.1.1");

        var join = Assert.IsType<JoinedEvent>(evt);
        Assert.Equal(920587237, join.PlaceId);
        Assert.Equal("f3a1c2d4-1111-2222-3333-444455556666", join.JobId);
    }

    [Fact]
    public void Universe_line_gives_place_and_universe()
    {
        var evt = RobloxLogParser.ParseLine(Prefix +
            "[FLog::GameJoinLoadTime] Report game_join_loadtime: placeid:920587237, visitid:0, userid:1, universeid:383310974, status:Success");

        var universe = Assert.IsType<UniverseEvent>(evt);
        Assert.Equal(920587237, universe.PlaceId);
        Assert.Equal(383310974, universe.UniverseId);
    }

    [Theory]
    [InlineData("[FLog::Network] Time to disconnect replication data: 0.002")]
    [InlineData("[FLog::SingleSurfaceApp] leaveUGCGameInternal")]
    [InlineData("[FLog::Network] Client:Disconnect 128.116.1.1|55812")]
    [InlineData("[FLog::Network] NetworkClient:Remove")]
    public void Leave_lines_are_detected(string message)
    {
        Assert.IsType<LeftEvent>(RobloxLogParser.ParseLine(Prefix + message));
    }

    [Fact]
    public void Unrelated_line_gives_no_event()
    {
        Assert.Null(RobloxLogParser.ParseLine(Prefix + "[FLog::Output] Settings loaded"));
    }
}
