namespace Companion.Core;

public sealed class PetState
{
    public string? PetName { get; set; }
    public string? UserName { get; set; }
    public bool Hatched { get; set; }
    public double Attention { get; set; } = 80;
    public double Energy { get; set; } = 80;
    public double? PositionX { get; set; }
    public double? PositionY { get; set; }
    public PerchKind Perch { get; set; } = PerchKind.Floor;
}

public sealed class PetStateStore
{
    private readonly string _path;

    public PetStateStore(string path, IClock clock, ILog log)
    {
        _path = path;
        Current = JsonFile.Load(path, () => new PetState(), log, clock);
    }

    /// <summary>Live object shared with PetLife; never replaced, only mutated.</summary>
    public PetState Current { get; }

    public void Save() => JsonFile.Save(_path, Current);

    public void Reset()
    {
        var fresh = new PetState();
        Current.PetName = fresh.PetName;
        Current.UserName = fresh.UserName;
        Current.Hatched = fresh.Hatched;
        Current.Attention = fresh.Attention;
        Current.Energy = fresh.Energy;
        Current.PositionX = fresh.PositionX;
        Current.PositionY = fresh.PositionY;
        Current.Perch = fresh.Perch;
        Save();
    }
}
