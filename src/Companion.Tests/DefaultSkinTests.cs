using System.Buffers.Binary;
using System.IO;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class DefaultSkinTests
{
    [Fact]
    public void Default_skin_png_matches_its_json()
    {
        var skinDir = Path.Combine(AppPaths.Resolve(AppContext.BaseDirectory).Skins, "default");
        var skin = SkinDefinition.Parse(File.ReadAllText(Path.Combine(skinDir, "sheet.json")));
        var png = File.ReadAllBytes(Path.Combine(skinDir, "sheet.png"));

        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        var frameCount = width / skin.FrameSize * (height / skin.FrameSize);

        Assert.Equal(64, width);
        Assert.Equal(160, height);
        Assert.All(skin.Animations.Values.SelectMany(a => a.Frames), f => Assert.InRange(f, 0, frameCount - 1));
        foreach (var name in new[] { "idle", "walk", "happy", "bored", "sleepy", "sleep", "thinking", "talking", "egg", "hang", "poof" })
            Assert.True(skin.Animations.ContainsKey(name), $"missing animation {name}");
    }
}
