using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class PetLifeTests
{
    private static readonly ActivitySnapshot Present = new(TimeSpan.FromSeconds(5), false);

    private static (PetLife Life, FakeClock Clock, PetState State) NewPet(int hour = 14, bool hatched = true)
    {
        var clock = FakeClock.At(2026, 9, 24, hour);
        var state = new PetState { Hatched = hatched };
        return (new PetLife(state, clock, new Random(7)), clock, state);
    }

    [Fact]
    public void Unhatched_pet_is_an_egg_until_hatched_then_happy()
    {
        var (life, clock, _) = NewPet(hatched: false);
        Assert.Equal(PetStateKind.Egg, life.Tick(Present).State);

        life.Hatch();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PetStateKind.Happy, life.Tick(Present).State);
    }

    [Fact]
    public void Click_makes_it_happy_for_three_seconds()
    {
        var (life, clock, state) = NewPet();
        life.OnClick();
        Assert.Equal(PetStateKind.Happy, life.Tick(Present).State);
        Assert.Equal(95, state.Attention, 3);

        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(PetStateKind.Idle, life.Tick(Present).State);
    }

    [Fact]
    public void Low_attention_is_bored_and_low_energy_is_sleepy()
    {
        var (life, _, state) = NewPet();
        state.Attention = 20;
        Assert.Equal(PetStateKind.Bored, life.Tick(Present).State);

        state.Energy = 10;
        Assert.Equal(PetStateKind.Sleepy, life.Tick(Present).State);
    }

    [Fact]
    public void Sleeps_after_five_idle_minutes_and_yawns_when_the_user_returns()
    {
        var (life, clock, _) = NewPet();
        Assert.Equal(PetStateKind.Sleepy, life.Tick(new(TimeSpan.FromMinutes(4), false)).State);
        Assert.Equal(PetStateKind.Sleep, life.Tick(new(TimeSpan.FromMinutes(6), false)).State);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PetStateKind.Sleepy, life.Tick(new(TimeSpan.FromSeconds(1), false)).State);

        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(PetStateKind.Idle, life.Tick(Present).State);
    }

    [Fact]
    public void At_night_it_sleeps_after_one_idle_minute()
    {
        var (life, _, _) = NewPet(hour: 2);
        Assert.NotEqual(PetStateKind.Sleep, life.Tick(new(TimeSpan.FromSeconds(30), false)).State);
        Assert.Equal(PetStateKind.Sleep, life.Tick(new(TimeSpan.FromMinutes(2), false)).State);
    }

    [Fact]
    public void Forced_sleep_lasts_until_clicked()
    {
        var (life, clock, _) = NewPet();
        life.ForceSleep();
        Assert.Equal(PetStateKind.Sleep, life.Tick(Present).State);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(PetStateKind.Sleep, life.Tick(Present).State);

        life.OnClick();
        Assert.Equal(PetStateKind.Happy, life.Tick(Present).State);
    }

    [Fact]
    public void Chat_states_override_everything_and_finishing_makes_it_happy()
    {
        var (life, _, state) = NewPet();
        state.Attention = 10;
        state.Energy = 5;

        life.OnChatStarted();
        Assert.Equal(PetStateKind.Thinking, life.Tick(Present).State);
        life.OnFirstToken();
        Assert.Equal(PetStateKind.Talking, life.Tick(Present).State);
        life.OnChatFinished(success: true);
        Assert.Equal(PetStateKind.Happy, life.Tick(Present).State);
        Assert.Equal(40, state.Attention, 1);
    }

    [Fact]
    public void Walks_within_a_minute_in_a_valid_direction()
    {
        var (life, clock, _) = NewPet();
        var walked = false;
        for (var i = 0; i < 61 && !walked; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            walked = life.Tick(Present).State == PetStateKind.Walk;
        }

        Assert.True(walked);
        Assert.Contains(life.WalkDirection, new[] { -1, 1 });
        var before = life.WalkDirection;
        life.ReverseWalk();
        Assert.Equal(-before, life.WalkDirection);
    }

    [Fact]
    public void Bored_line_appears_at_most_every_twenty_minutes_and_only_when_present()
    {
        var (life, clock, state) = NewPet();
        var lines = new List<int>();
        for (var minute = 1; minute <= 45; minute++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            state.Attention = 20;
            if (life.Tick(Present).Line is not null) lines.Add(minute);
        }
        Assert.Equal(new[] { 20, 40 }, lines);

        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Null(life.Tick(new(TimeSpan.FromSeconds(1), false)).Line);   // typing right now
        Assert.Null(life.Tick(new(TimeSpan.FromSeconds(5), true)).Line);    // fullscreen
        Assert.NotNull(life.Tick(Present).Line);
    }

    [Fact]
    public void Tick_reports_suspension()
    {
        var (life, _, _) = NewPet();
        Assert.True(life.Tick(new(TimeSpan.Zero, true)).Suspended);
    }
}
