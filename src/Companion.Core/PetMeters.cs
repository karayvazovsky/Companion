namespace Companion.Core;

/// <summary>Light-care needs. They only influence mood; nothing bad ever happens.</summary>
public sealed class PetMeters(PetState state)
{
    public const double AttentionDrainPerMinute = 0.5;      // −1 per 2 min while the user is at the PC
    public const double EnergyDrainPerMinute = 1.0 / 3.0;   // −1 per 3 min while awake
    public const double EnergyGainPerMinuteAsleep = 2;
    public const double ClickAttention = 15;
    public const double ChatAttention = 30;

    public double Attention => state.Attention;
    public double Energy => state.Energy;

    public void Tick(TimeSpan elapsed, bool userActive, bool asleep)
    {
        var minutes = Math.Max(0, elapsed.TotalMinutes);
        if (userActive && !asleep) state.Attention = Clamp(state.Attention - AttentionDrainPerMinute * minutes);
        state.Energy = Clamp(asleep
            ? state.Energy + EnergyGainPerMinuteAsleep * minutes
            : state.Energy - EnergyDrainPerMinute * minutes);
    }

    public void OnClick() => state.Attention = Clamp(state.Attention + ClickAttention);

    public void OnChat() => state.Attention = Clamp(state.Attention + ChatAttention);

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
}
