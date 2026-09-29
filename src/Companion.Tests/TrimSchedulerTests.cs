using Xunit;

namespace Companion.Tests;

public class TrimSchedulerTests
{
    [Fact]
    public void Trims_when_quiet_and_stops_waiting()
    {
        var trims = 0;
        using var scheduler = new TrimScheduler(() => false, () => trims++);
        scheduler.Request();
        Assert.True(scheduler.IsPending);

        scheduler.OnDebounceElapsed();

        Assert.Equal(1, trims);
        Assert.False(scheduler.IsPending);
    }

    [Fact]
    public void Waits_another_round_while_busy_then_trims_once_quiet()
    {
        var trims = 0;
        var busy = true;
        using var scheduler = new TrimScheduler(() => busy, () => trims++);
        scheduler.Request();

        scheduler.OnDebounceElapsed();
        Assert.Equal(0, trims);
        Assert.True(scheduler.IsPending);

        busy = false;
        scheduler.OnDebounceElapsed();
        Assert.Equal(1, trims);
        Assert.False(scheduler.IsPending);
    }

    [Fact]
    public void Does_nothing_after_dispose()
    {
        var trims = 0;
        var scheduler = new TrimScheduler(() => false, () => trims++);
        scheduler.Request();
        scheduler.Dispose();
        Assert.False(scheduler.IsPending);

        scheduler.Request();
        scheduler.Start();
        scheduler.OnDebounceElapsed();

        Assert.False(scheduler.IsPending);
        Assert.Equal(0, trims);
    }
}
