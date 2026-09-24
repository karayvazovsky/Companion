using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class SpendTests
{
    [Fact]
    public void Cost_uses_opus_5_5_prices()
    {
        Assert.Equal(4.00m, Pricing.Cost(new UsageTotals(1_000_000, 0, 0, 0, 0)));
        Assert.Equal(20.00m, Pricing.Cost(new UsageTotals(0, 1_000_000, 0, 0, 0)));
        Assert.Equal(0.20m, Pricing.Cost(new UsageTotals(0, 0, 1_000_000, 0, 0)));
        Assert.Equal(5.00m, Pricing.Cost(new UsageTotals(0, 0, 0, 1_000_000, 0)));
        Assert.Equal(0.03m, Pricing.Cost(new UsageTotals(0, 0, 0, 0, 3)));
        Assert.Equal(0.014m, Pricing.Cost(new UsageTotals(2000, 300, 0, 0, 0)));
    }

    [Fact]
    public void Usage_totals_add_up()
    {
        var sum = new UsageTotals(1, 2, 3, 4, 5) + new UsageTotals(10, 20, 30, 40, 50);
        Assert.Equal(new UsageTotals(11, 22, 33, 44, 55), sum);
    }

    [Fact]
    public void Record_accumulates_per_month_and_persists()
    {
        using var tmp = new TempDir();
        var clock = FakeClock.At(2026, 9, 24, 12);
        var settings = new SettingsStore(tmp.PathOf("settings.json"), clock, NullLog.Instance);
        var spend = new SpendTracker(settings, clock);

        spend.Record(new UsageTotals(2000, 300, 0, 0, 0));
        spend.Record(new UsageTotals(2000, 300, 0, 0, 0));
        Assert.Equal(0.028m, spend.ThisMonth);

        clock.Advance(TimeSpan.FromDays(10));
        Assert.Equal(0m, spend.ThisMonth);
        var reloaded = new SettingsStore(tmp.PathOf("settings.json"), clock, NullLog.Instance);
        Assert.Equal(0.028m, reloaded.Current.SpendByMonth["2026-09"]);
    }

    [Fact]
    public void Cap_and_one_time_warning_at_80_percent()
    {
        using var tmp = new TempDir();
        var clock = FakeClock.At(2026, 9, 24, 12);
        var settings = new SettingsStore(tmp.PathOf("settings.json"), clock, NullLog.Instance);
        settings.Current.MonthlyCapUsd = 1.00m;
        var spend = new SpendTracker(settings, clock);

        settings.Current.SpendByMonth["2026-09"] = 0.79m;
        Assert.False(spend.TakeWarning());
        settings.Current.SpendByMonth["2026-09"] = 0.85m;
        Assert.True(spend.TakeWarning());
        Assert.False(spend.TakeWarning());
        Assert.False(spend.CapReached);

        settings.Current.SpendByMonth["2026-09"] = 1.00m;
        Assert.True(spend.CapReached);
    }
}
