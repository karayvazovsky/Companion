namespace Companion.Core;

public enum PetStateKind { Egg, Idle, Walk, Happy, Bored, Sleepy, Sleep, Thinking, Talking }

/// <param name="UserIdle">Time since the last keyboard/mouse input.</param>
/// <param name="Suspended">A fullscreen app is in front or the session is locked.</param>
public readonly record struct ActivitySnapshot(TimeSpan UserIdle, bool Suspended);

public interface IUserActivity
{
    ActivitySnapshot Sample();
}

public sealed record PetTick(PetStateKind State, bool Suspended, string? Line);

/// <summary>
/// The pet's brainless half: decides state once per second from time, meters and user activity.
/// Priority: Thinking/Talking &gt; Happy &gt; Sleep &gt; Sleepy &gt; Bored &gt; Walk &gt; Idle.
/// </summary>
public sealed class PetLife
{
    public static readonly TimeSpan HappyDuration = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan WakeYawnDuration = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan LineCooldown = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan SleepyAfterIdle = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan SleepAfterIdle = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NightSleepAfterIdle = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan PresentWithin = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan TypingGuard = TimeSpan.FromSeconds(2);

    private enum ChatPhase { None, Thinking, Talking }

    private readonly PetState _state;
    private readonly IClock _clock;
    private readonly Random _rng;
    private DateTimeOffset _lastTick;
    private DateTimeOffset _lastLine;
    private DateTimeOffset _happyUntil;
    private DateTimeOffset _yawnUntil;
    private DateTimeOffset _walkUntil;
    private DateTimeOffset _nextWalkAt;
    private bool _forcedSleep;
    private bool _wasAsleep;
    private ChatPhase _chat;

    public PetLife(PetState state, IClock clock, Random rng)
    {
        _state = state;
        _clock = clock;
        _rng = rng;
        Meters = new PetMeters(state);
        var now = clock.Now;
        _lastTick = now;
        _lastLine = now;
        _nextWalkAt = now + NextWalkDelay();
        State = state.Hatched ? PetStateKind.Idle : PetStateKind.Egg;
    }

    public PetMeters Meters { get; }

    public PetStateKind State { get; private set; }

    public int WalkDirection { get; private set; } = 1;

    public PetTick Tick(ActivitySnapshot activity)
    {
        var now = _clock.Now;
        var elapsed = now - _lastTick;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        _lastTick = now;

        if (!_state.Hatched)
        {
            State = PetStateKind.Egg;
            return new PetTick(State, activity.Suspended, null);
        }

        Meters.Tick(elapsed, activity.UserIdle < PresentWithin, State == PetStateKind.Sleep);
        State = Decide(activity, now);

        string? line = null;
        if (!activity.Suspended
            && activity.UserIdle >= TypingGuard
            && activity.UserIdle < PresentWithin
            && now - _lastLine >= LineCooldown)
        {
            line = PetLines.Pick(State, _rng);
            if (line is not null) _lastLine = now;
        }
        return new PetTick(State, activity.Suspended, line);
    }

    public void OnClick()
    {
        if (!_state.Hatched) return;
        _forcedSleep = false;
        Meters.OnClick();
        _happyUntil = _clock.Now + HappyDuration;
    }

    public void Hatch()
    {
        _state.Hatched = true;
        _happyUntil = _clock.Now + HappyDuration;
    }

    public void ForceSleep() => _forcedSleep = true;

    public void OnChatStarted()
    {
        _forcedSleep = false;
        _chat = ChatPhase.Thinking;
    }

    public void OnFirstToken() => _chat = ChatPhase.Talking;

    public void OnChatFinished(bool success)
    {
        _chat = ChatPhase.None;
        if (!success) return;
        Meters.OnChat();
        _happyUntil = _clock.Now + HappyDuration;
    }

    public void ReverseWalk() => WalkDirection = -WalkDirection;

    private PetStateKind Decide(ActivitySnapshot activity, DateTimeOffset now)
    {
        if (_chat == ChatPhase.Thinking) return PetStateKind.Thinking;
        if (_chat == ChatPhase.Talking) return PetStateKind.Talking;
        if (now < _happyUntil) return PetStateKind.Happy;

        var sleep = _forcedSleep
            || activity.UserIdle >= SleepAfterIdle
            || (now.Hour < 7 && activity.UserIdle >= NightSleepAfterIdle);
        if (_wasAsleep && !sleep) _yawnUntil = now + WakeYawnDuration;
        _wasAsleep = sleep;
        if (sleep) return PetStateKind.Sleep;
        if (now < _yawnUntil) return PetStateKind.Sleepy;
        if (Meters.Energy < 25 || activity.UserIdle >= SleepyAfterIdle) return PetStateKind.Sleepy;
        if (Meters.Attention < 30) return PetStateKind.Bored;
        if (now < _walkUntil) return PetStateKind.Walk;
        if (now >= _nextWalkAt)
        {
            _walkUntil = now + TimeSpan.FromSeconds(2 + _rng.NextDouble() * 2);
            _nextWalkAt = _walkUntil + NextWalkDelay();
            WalkDirection = _rng.Next(2) == 0 ? -1 : 1;
            return PetStateKind.Walk;
        }
        return PetStateKind.Idle;
    }

    private TimeSpan NextWalkDelay() => TimeSpan.FromSeconds(20 + _rng.NextDouble() * 40);
}

/// <summary>Built-in lines shown in a mini bubble. Local only: they never cost API money.</summary>
public static class PetLines
{
    public static readonly string[] Bored =
    [
        "psst… anything I can help with?",
        "*sniffs around the taskbar*",
        "I could look something up for you!",
        "*wags* ask me anything!",
        "is it snack o'clock?",
    ];

    public static readonly string[] Sleepy =
    [
        "*yaaawn*",
        "long day, huh?",
        "*rubs eyes*",
    ];

    public static string? Pick(PetStateKind state, Random rng) => state switch
    {
        PetStateKind.Bored => Bored[rng.Next(Bored.Length)],
        PetStateKind.Sleepy => Sleepy[rng.Next(Sleepy.Length)],
        _ => null,
    };
}
