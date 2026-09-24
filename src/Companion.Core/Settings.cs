namespace Companion.Core;

public enum EffortLevel { Low, Medium, High }

public sealed class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public EffortLevel EverydayEffort { get; set; } = EffortLevel.Low;
    public EffortLevel ThinkHarderEffort { get; set; } = EffortLevel.High;
    public decimal MonthlyCapUsd { get; set; } = 30m;
    public int PetScale { get; set; } = 4;
    public bool StartWithWindows { get; set; }

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
    }
}
