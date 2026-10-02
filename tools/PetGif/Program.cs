using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Companion.Core;

// Renders skin animations into one looping animated GIF, for devlogs and release notes.
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: PetGif <skin folder> <output.gif> [--anims idle,walk,...] [--scale 8] [--pad 2] [--seconds 2] [--bg #RRGGBB|none]");
    return 1;
}

try
{
    var options = Options.Parse(args);
    var skin = SkinDefinition.Parse(File.ReadAllText(Path.Combine(options.SkinFolder, "sheet.json")));
    var sheet = Sheet.Load(Path.Combine(options.SkinFolder, "sheet.png"));
    var names = options.Animations ?? skin.Animations.Keys.ToArray();
    var unknown = names.Where(n => !skin.Animations.ContainsKey(n)).ToArray();
    if (unknown.Length > 0)
        throw new ArgumentException($"Unknown animation(s): {string.Join(", ", unknown)}. Known: {string.Join(", ", skin.Animations.Keys)}");

    var timeline = Timeline.Build(skin, names, options.Seconds);
    var renderer = new Renderer(sheet, skin.FrameSize, options.Scale, options.Pad, options.Background);
    var frames = Timeline.Merge(timeline.Select(step => (renderer.Render(step.Frame), step.Centiseconds)));
    GifWriter.Write(Path.GetFullPath(options.Output), renderer.Width, renderer.Height, renderer.Palette, options.Background is null, frames);
    Console.WriteLine($"Wrote {frames.Count} frames ({string.Join(", ", names)}) to {Path.GetFullPath(options.Output)}");
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or NotSupportedException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

sealed record Options(string SkinFolder, string Output, string[]? Animations, int Scale, int Pad, double Seconds, Color? Background)
{
    public static Options Parse(string[] args)
    {
        string[]? animations = null;
        int scale = 8, pad = 2;
        double seconds = 2;
        Color? background = Color.FromRgb(0xF4, 0xEF, 0xE6);
        for (var i = 2; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--anims": animations = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                case "--scale": scale = Math.Clamp(int.Parse(value), 1, 32); break;
                case "--pad": pad = Math.Clamp(int.Parse(value), 0, 16); break;
                case "--seconds": seconds = Math.Clamp(double.Parse(value, System.Globalization.CultureInfo.InvariantCulture), 0.5, 10); break;
                case "--bg": background = value == "none" ? null : (Color)ColorConverter.ConvertFromString(value); break;
                default: throw new ArgumentException($"Unknown option {args[i]}.");
            }
            i++;
        }
        return new Options(args[0], args[1], animations, scale, pad, seconds, background);
    }
}

/// <summary>The sprite sheet as BGRA bytes.</summary>
sealed class Sheet(byte[] pixels, int width, int height)
{
    public int Width => width;
    public int Height => height;

    public static Sheet Load(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        return new Sheet(pixels, bgra.PixelWidth, bgra.PixelHeight);
    }

    /// <summary>Opaque colour at (x, y), or null when the pixel is mostly transparent or off the sheet.</summary>
    public Color? At(int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return null;
        var i = (y * width + x) * 4;
        return pixels[i + 3] < 128 ? null : Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
    }
}

static class Timeline
{
    /// <summary>Looping animations play for at least <paramref name="seconds"/>; one-shots play once and hold.</summary>
    public static List<(int Frame, int Centiseconds)> Build(SkinDefinition skin, IEnumerable<string> names, double seconds)
    {
        var steps = new List<(int, int)>();
        foreach (var name in names)
        {
            var animation = skin.Animations[name];
            var delay = Math.Max(2, (int)Math.Round(100.0 / animation.Fps));
            var cycles = animation.Loop ? Math.Max(1, (int)Math.Ceiling(seconds * 100 / (delay * animation.Frames.Length))) : 1;
            for (var c = 0; c < cycles; c++)
                foreach (var frame in animation.Frames) steps.Add((frame, delay));
            if (!animation.Loop) steps[^1] = (steps[^1].Item1, steps[^1].Item2 + 50);
        }
        return steps;
    }

    /// <summary>Folds identical consecutive images into one longer frame.</summary>
    public static List<(byte[] Indices, int Centiseconds)> Merge(IEnumerable<(byte[] Indices, int Centiseconds)> frames)
    {
        var merged = new List<(byte[], int)>();
        foreach (var (indices, delay) in frames)
        {
            if (merged.Count > 0 && merged[^1].Item1.AsSpan().SequenceEqual(indices))
                merged[^1] = (merged[^1].Item1, merged[^1].Item2 + delay);
            else
                merged.Add((indices, delay));
        }
        return merged;
    }
}

/// <summary>Cuts frames from the sheet, scales them with nearest neighbour and maps colours to one shared palette.</summary>
sealed class Renderer(Sheet sheet, int frameSize, int scale, int pad, Color? background)
{
    private readonly Dictionary<Color, byte> _lookup = new();

    /// <summary>Index 0 is the background (or the transparent colour).</summary>
    public List<Color> Palette { get; } = [background ?? Colors.Black];
    public int Width => (frameSize + pad * 2) * scale;
    public int Height => Width;

    public byte[] Render(int frame)
    {
        var (originX, originY) = SkinDefinition.FrameOrigin(frame, frameSize, sheet.Width);
        var indices = new byte[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            var sy = y / scale - pad;
            for (var x = 0; x < Width; x++)
            {
                var sx = x / scale - pad;
                var inside = sx >= 0 && sy >= 0 && sx < frameSize && sy < frameSize;
                var color = inside ? sheet.At(originX + sx, originY + sy) : null;
                indices[y * Width + x] = color is { } c ? IndexOf(c) : (byte)0;
            }
        }
        return indices;
    }

    private byte IndexOf(Color color)
    {
        if (_lookup.TryGetValue(color, out var index)) return index;
        if (Palette.Count == 256) throw new NotSupportedException("The skin uses more than 255 colours; GIF can't hold them.");
        index = (byte)Palette.Count;
        Palette.Add(color);
        _lookup[color] = index;
        return index;
    }
}

/// <summary>Minimal GIF89a writer: one global palette, infinite loop, LZW-compressed frames.</summary>
static class GifWriter
{
    public static void Write(string path, int width, int height, List<Color> palette, bool transparent, List<(byte[] Indices, int Centiseconds)> frames)
    {
        var tableBits = 1;
        while (1 << tableBits < palette.Count) tableBits++;
        var minCodeSize = Math.Max(2, tableBits);

        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        w.Write("GIF89a"u8);
        w.Write((ushort)width);
        w.Write((ushort)height);
        w.Write((byte)(0x80 | 0x70 | (tableBits - 1)));
        w.Write((byte)0);
        w.Write((byte)0);
        for (var i = 0; i < 1 << tableBits; i++)
        {
            var c = i < palette.Count ? palette[i] : Colors.Black;
            w.Write(c.R); w.Write(c.G); w.Write(c.B);
        }
        w.Write([0x21, 0xFF, 0x0B]);
        w.Write("NETSCAPE2.0"u8);
        w.Write([0x03, 0x01, 0x00, 0x00, 0x00]);

        foreach (var (indices, delay) in frames)
        {
            // Disposal 2 clears to background so transparent pixels never show the previous frame.
            w.Write([0x21, 0xF9, 0x04, (byte)((2 << 2) | (transparent ? 1 : 0))]);
            w.Write((ushort)delay);
            w.Write([(byte)0, (byte)0]);
            w.Write((byte)0x2C);
            w.Write((ushort)0); w.Write((ushort)0);
            w.Write((ushort)width); w.Write((ushort)height);
            w.Write((byte)0);
            w.Write((byte)minCodeSize);
            var data = Lzw(indices, minCodeSize);
            for (var offset = 0; offset < data.Length; offset += 255)
            {
                var length = Math.Min(255, data.Length - offset);
                w.Write((byte)length);
                w.Write(data, offset, length);
            }
            w.Write((byte)0);
        }
        w.Write((byte)0x3B);
    }

    private static byte[] Lzw(byte[] indices, int minCodeSize)
    {
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var codeSize = minCodeSize + 1;
        var next = end + 1;
        var table = new Dictionary<int, int>();
        var output = new List<byte>();
        int buffer = 0, bits = 0;

        void Emit(int code)
        {
            buffer |= code << bits;
            bits += codeSize;
            while (bits >= 8)
            {
                output.Add((byte)buffer);
                buffer >>= 8;
                bits -= 8;
            }
        }

        Emit(clear);
        var prefix = (int)indices[0];
        for (var i = 1; i < indices.Length; i++)
        {
            var key = (prefix << 8) | indices[i];
            if (table.TryGetValue(key, out var code))
            {
                prefix = code;
                continue;
            }
            Emit(prefix);
            if (next == 4096)
            {
                Emit(clear);
                table.Clear();
                codeSize = minCodeSize + 1;
                next = end + 1;
            }
            else
            {
                if (next >= 1 << codeSize) codeSize++;
                table[key] = next++;
            }
            prefix = indices[i];
        }
        Emit(prefix);
        Emit(end);
        if (bits > 0) output.Add((byte)buffer);
        return output.ToArray();
    }
}
