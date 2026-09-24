using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class SkinTests
{
    private const string Json = """
        {
          "frameSize": 16,
          "animations": {
            "idle":  { "fps": 4, "loop": true,  "frames": [0, 1, 2, 1] },
            "happy": { "fps": 8, "loop": false, "frames": [8, 9, 10] }
          }
        }
        """;

    [Fact]
    public void Parse_reads_frames_and_falls_back_to_idle()
    {
        var skin = SkinDefinition.Parse(Json);

        Assert.Equal(16, skin.FrameSize);
        Assert.Equal(new[] { 8, 9, 10 }, skin.Get("happy").Frames);
        Assert.Same(skin.Get("idle"), skin.Get("walk"));
    }

    [Theory]
    [InlineData("""{ "frameSize": 16, "animations": { "walk": { "fps": 4, "loop": true, "frames": [0] } } }""")]
    [InlineData("""{ "frameSize": 0, "animations": { "idle": { "fps": 4, "loop": true, "frames": [0] } } }""")]
    [InlineData("""{ "frameSize": 16, "animations": { "idle": { "fps": 0, "loop": true, "frames": [0] } } }""")]
    [InlineData("""{ "frameSize": 16, "animations": { "idle": { "fps": 4, "loop": true, "frames": [] } } }""")]
    [InlineData("not json")]
    public void Parse_rejects_invalid_skins(string json)
    {
        Assert.Throws<FormatException>(() => SkinDefinition.Parse(json));
    }

    [Fact]
    public void FrameOrigin_is_row_major()
    {
        Assert.Equal((0, 0), SkinDefinition.FrameOrigin(0, 16, 64));
        Assert.Equal((48, 0), SkinDefinition.FrameOrigin(3, 16, 64));
        Assert.Equal((16, 16), SkinDefinition.FrameOrigin(5, 16, 64));
    }

    [Fact]
    public void Player_loops_and_clamps_non_looping_animations()
    {
        var player = new AnimationPlayer(SkinDefinition.Parse(Json));

        player.Play("idle", TimeSpan.FromSeconds(10));
        Assert.Equal("idle", player.Current);
        Assert.Equal(0, player.FrameAt(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, player.FrameAt(TimeSpan.FromSeconds(10.25)));
        Assert.Equal(0, player.FrameAt(TimeSpan.FromSeconds(11)));
        Assert.Equal(TimeSpan.FromSeconds(0.25), player.FrameInterval);

        player.Play("happy", TimeSpan.Zero);
        Assert.Equal(10, player.FrameAt(TimeSpan.FromSeconds(5)));
    }
}
