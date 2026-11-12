using RobloxTracker.Core.Services;

namespace RobloxTracker.Tests;

public sealed class AntiAfkScheduleTests
{
    [Fact]
    public void Delay_stays_between_the_two_bounds()
    {
        var random = new Random(1);
        for (var i = 0; i < 500; i++)
        {
            var delay = AntiAfkSchedule.NextDelay(10, 15, random);
            Assert.InRange(delay, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15));
        }
    }

    [Fact]
    public void Inverted_bounds_are_swapped()
    {
        var delay = AntiAfkSchedule.NextDelay(15, 10, new Random(1));
        Assert.InRange(delay, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Delay_is_at_least_one_minute_and_leaves_room_for_the_warning()
    {
        var delay = AntiAfkSchedule.NextDelay(0, -3, new Random(1));
        Assert.Equal(TimeSpan.FromMinutes(1), delay);
        Assert.True(delay > TimeSpan.FromSeconds(AntiAfkSchedule.WarningSeconds));
    }

    [Fact]
    public void Another_window_in_front_requires_a_switch()
    {
        Assert.Equal(AntiAfkAction.SwitchWindow, AntiAfkSchedule.Decide(false, TimeSpan.Zero));
        Assert.Equal(AntiAfkAction.SwitchWindow, AntiAfkSchedule.Decide(false, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Nothing_happens_while_the_user_is_playing()
    {
        Assert.Equal(AntiAfkAction.Skip, AntiAfkSchedule.Decide(true, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Idle_user_on_roblox_gets_a_press_without_switching()
    {
        Assert.Equal(AntiAfkAction.PressInPlace, AntiAfkSchedule.Decide(true, TimeSpan.FromMinutes(2)));
    }
}
