namespace Companion.Core;

public enum EffortLevel { Low, Medium, High }

public enum BrainKind { Subscription, ApiKey }

public sealed class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public EffortLevel EverydayEffort { get; set; } = EffortLevel.Low;
    public EffortLevel ThinkHarderEffort { get; set; } = EffortLevel.High;
    public decimal MonthlyCapUsd { get; set; } = 30m;
    public int PetScale { get; set; } = 4;
    public bool StartWithWindows { get; set; }

    /// <summary>Where answers come from: the user's Claude subscription via Claude Code (default) or a paid API key.</summary>
    public BrainKind Brain { get; set; } = BrainKind.Subscription;
    public string SubscriptionEverydayModel { get; set; } = "claude-sonnet-5";
    public string SubscriptionThinkHarderModel { get; set; } = "claude-opus-5-5";
    /// <summary>Optional full path to claude.exe; empty means auto-detect.</summary>
    public string? ClaudeCodePath { get; set; }
    public bool AutoTeleport { get; set; } = true;

    /// <summary>Estimated spend per calendar month, keyed "yyyy-MM".</summary>
    public Dictionary<string, decimal> SpendByMonth { get; set; } = new();

    /// <summary>Month key for which the 80 % warning was already shown.</summary>
    public string? CapWarnedMonth { get; set; }
}

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string path, IClock clock, ILog log)
    {
        _path = path;
        Current = JsonFile.Load(path, () => new AppSettings(), log, clock);
        Normalize();
    }

    public AppSettings Current { get; }

    public event Action? Changed;

    public void Save()
    {
        Normalize();
        JsonFile.Save(_path, Current);
        Changed?.Invoke();
    }

    private void Normalize()
    {
        if (Current.PetScale is not (3 or 4 or 6)) Current.PetScale = 4;
        if (Current.MonthlyCapUsd < 0) Current.MonthlyCapUsd = 0;
        if (string.IsNullOrWhiteSpace(Current.Hotkey)) Current.Hotkey = "Ctrl+Alt+Space";
        Current.SpendByMonth ??= new();
        if (string.IsNullOrWhiteSpace(Current.SubscriptionEverydayModel)) Current.SubscriptionEverydayModel = "claude-sonnet-5";
        if (string.IsNullOrWhiteSpace(Current.SubscriptionThinkHarderModel)) Current.SubscriptionThinkHarderModel = "claude-opus-5-5";
        Current.SubscriptionEverydayModel = Current.SubscriptionEverydayModel.Trim();
        Current.SubscriptionThinkHarderModel = Current.SubscriptionThinkHarderModel.Trim();
    }
}
