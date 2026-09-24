using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class PetMetersTests
{
    [Fact]
    public void Attention_drains_only_while_the_user_is_active_and_pet_awake()
    {
        var state = new PetState();
        var meters = new PetMeters(state);

        meters.Tick(TimeSpan.FromMinutes(10), userActive: true, asleep: false);
        Assert.Equal(75, meters.Attention, 3);

        meters.Tick(TimeSpan.FromMinutes(10), userActive: false, asleep: false);
        Assert.Equal(75, meters.Attention, 3);
    }

    [Fact]
    public void Energy_drains_awake_and_refills_asleep_clamped_to_100()
    {
        var state = new PetState();
        var meters = new PetMeters(state);

        meters.Tick(TimeSpan.FromMinutes(30), userActive: false, asleep: false);
        Assert.Equal(70, meters.Energy, 3);

        meters.Tick(TimeSpan.FromMinutes(60), userActive: false, asleep: true);
        Assert.Equal(100, meters.Energy, 3);
        Assert.Equal(100, state.Energy, 3);
    }

    [Fact]
    public void Click_and_chat_add_attention_clamped()
    {
        var state = new PetState { Attention = 10 };
        var meters = new PetMeters(state);

        meters.OnClick();
        Assert.Equal(25, meters.Attention, 3);
        meters.OnChat();
        meters.OnChat();
        meters.OnChat();
        Assert.Equal(100, meters.Attention, 3);
    }
}
