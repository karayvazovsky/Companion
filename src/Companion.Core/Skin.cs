using System.Text.Json;

namespace Companion.Core;

public sealed record AnimationDef(int Fps, bool Loop, int[] Frames);

/// <summary>Contents of skins\&lt;name&gt;\sheet.json.</summary>
public sealed class SkinDefinition
{
    public int FrameSize { get; init; }
    public Dictionary<string, AnimationDef> Animations { get; init; } = new();

    public static SkinDefinition Parse(string json)
    {
        SkinDefinition? skin;
        try { skin = JsonSerializer.Deserialize<SkinDefinition>(json, JsonFile.Options); }
        catch (JsonException ex) { throw new FormatException("sheet.json is not valid JSON.", ex); }

        if (skin is null) throw new FormatException("sheet.json is empty.");
        if (skin.FrameSize <= 0) throw new FormatException("frameSize must be positive.");
        if (!skin.Animations.ContainsKey("idle")) throw new FormatException("An 'idle' animation is required.");
        foreach (var (name, animation) in skin.Animations)
        {
            if (animation.Fps is < 1 or > 60) throw new FormatException($"Animation '{name}' needs fps between 1 and 60.");
            if (animation.Frames is null || animation.Frames.Length == 0 || animation.Frames.Any(f => f < 0))
                throw new FormatException($"Animation '{name}' needs at least one non-negative frame.");
        }
        return skin;
    }

    public AnimationDef Get(string name) =>
        Animations.TryGetValue(name, out var animation) ? animation : Animations["idle"];

    public static (int X, int Y) FrameOrigin(int index, int frameSize, int sheetWidth)
    {
        var columns = Math.Max(1, sheetWidth / frameSize);
        return (index % columns * frameSize, index / columns * frameSize);
    }
}

/// <summary>Pure frame-timing logic; the window asks it which frame to show at a given time.</summary>
public sealed class AnimationPlayer(SkinDefinition skin)
{
    private TimeSpan _start;

    public string Current { get; private set; } = "";

    public TimeSpan FrameInterval => TimeSpan.FromSeconds(1.0 / skin.Get(Current).Fps);

    public void Play(string name, TimeSpan now)
    {
        Current = name;
        _start = now;
    }

    public int FrameAt(TimeSpan now)
    {
        var animation = skin.Get(Current);
        var step = (int)Math.Max(0, (now - _start).TotalSeconds * animation.Fps);
        return animation.Loop
            ? animation.Frames[step % animation.Frames.Length]
            : animation.Frames[Math.Min(step, animation.Frames.Length - 1)];
    }
}
