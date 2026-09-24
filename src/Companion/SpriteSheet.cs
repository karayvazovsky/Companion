using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Companion.Core;

namespace Companion;

public sealed class SpriteSheet
{
    private SpriteSheet(SkinDefinition definition, IReadOnlyList<BitmapSource> frames)
    {
        Definition = definition;
        Frames = frames;
    }

    public SkinDefinition Definition { get; }

    public IReadOnlyList<BitmapSource> Frames { get; }

    public BitmapSource Frame(int index) => Frames[((index % Frames.Count) + Frames.Count) % Frames.Count];

    public static SpriteSheet Load(string skinDirectory)
    {
        var definition = SkinDefinition.Parse(File.ReadAllText(Path.Combine(skinDirectory, "sheet.json")));
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(Path.Combine(skinDirectory, "sheet.png")));
        image.EndInit();
        image.Freeze();

        var size = definition.FrameSize;
        var count = image.PixelWidth / size * (image.PixelHeight / size);
        if (count == 0) throw new FormatException("sheet.png is smaller than one frame.");
        var frames = new List<BitmapSource>(count);
        for (var i = 0; i < count; i++)
        {
            var (x, y) = SkinDefinition.FrameOrigin(i, size, image.PixelWidth);
            var frame = new CroppedBitmap(image, new Int32Rect(x, y, size, size));
            frame.Freeze();
            frames.Add(frame);
        }
        return new SpriteSheet(definition, frames);
    }

    /// <summary>A plain orange block, used when the skin is missing or broken.</summary>
    public static SpriteSheet Fallback()
    {
        const int size = 16;
        var pixels = new uint[size * size];
        for (var y = 4; y < 14; y++)
            for (var x = 3; x < 13; x++)
                pixels[y * size + x] = 0xFFD97757;
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        var definition = SkinDefinition.Parse("""{ "frameSize": 16, "animations": { "idle": { "fps": 1, "loop": true, "frames": [0] } } }""");
        return new SpriteSheet(definition, [bitmap]);
    }
}
