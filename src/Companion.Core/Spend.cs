using System.Globalization;

namespace Companion.Core;

public readonly record struct UsageTotals(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, long WebSearches)
{
    public static UsageTotals operator +(UsageTotals a, UsageTotals b) => new(
        a.InputTokens + b.InputTokens,
        a.OutputTokens + b.OutputTokens,
        a.CacheReadTokens + b.CacheReadTokens,
        a.CacheWriteTokens + b.CacheWriteTokens,
        a.WebSearches + b.WebSearches);
}

/// <summary>Estimated USD prices for claude-opus-5-5. The Anthropic console is the source of truth.</summary>
public static class Pricing
{
    public const decimal InputPerMillion = 4.00m;
    public const decimal OutputPerMillion = 20.00m;
    public const decimal CacheReadPerMillion = 0.20m;
    public const decimal CacheWritePerMillion = 5.00m;
    public const decimal PerWebSearch = 0.01m;

    public static decimal Cost(UsageTotals usage) =>
        (usage.InputTokens * InputPerMillion
         + usage.OutputTokens * OutputPerMillion
         + usage.CacheReadTokens * CacheReadPerMillion
         + usage.CacheWriteTokens * CacheWritePerMillion) / 1_000_000m
        + usage.WebSearches * PerWebSearch;
}

/// <summary>Month-to-date spend estimate and the local cap (a cap of 0 blocks all questions).</summary>
public sealed class SpendTracker(SettingsStore settings, IClock clock)
{
    private string MonthKey => clock.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public decimal Cap => settings.Current.MonthlyCapUsd;

    public decimal ThisMonth => settings.Current.SpendByMonth.GetValueOrDefault(MonthKey);

    public bool CapReached => ThisMonth >= Cap;

    public decimal Record(UsageTotals usage)
    {
        var cost = Pricing.Cost(usage);
        if (cost <= 0) return 0;
        settings.Current.SpendByMonth[MonthKey] = ThisMonth + cost;
        settings.Save();
        return cost;
    }

    /// <summary>True exactly once per month, the first time spend reaches 80 % of the cap.</summary>
    public bool TakeWarning()
    {
        if (Cap <= 0 || ThisMonth < Cap * 0.8m || ThisMonth >= Cap || settings.Current.CapWarnedMonth == MonthKey) return false;
        settings.Current.CapWarnedMonth = MonthKey;
        settings.Save();
        return true;
    }
}
