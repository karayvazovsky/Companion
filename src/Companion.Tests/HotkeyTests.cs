using Companion;
using Xunit;

namespace Companion.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", 0x3u, 0x20u)]
    [InlineData(" alt + f1 ", 0x1u, 0x70u)]
    [InlineData("Win+Shift+K", 0xCu, 0x4Bu)]
    public void Parses_valid_hotkeys(string text, uint modifiers, uint virtualKey)
    {
        Assert.True(HotkeyService.TryParse(text, out var mods, out var vk));
        Assert.Equal(modifiers, mods);
        Assert.Equal(virtualKey, vk);
    }

    [Theory]
    [InlineData("Space")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+5")]
    [InlineData("")]
    public void Rejects_invalid_hotkeys(string text)
    {
        Assert.False(HotkeyService.TryParse(text, out _, out _));
    }
}
