using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: SkinGen <output sheet.png>");
    return 1;
}

var frames = Frames.Build();
var output = Path.GetFullPath(args[0]);
Exception? failure = null;
var thread = new Thread(() =>
{
    try { Frames.WritePng(frames, output); }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
if (failure is not null)
{
    Console.Error.WriteLine(failure);
    return 1;
}
Console.WriteLine($"Wrote {frames.Length} frames to {output}");
return 0;

enum EyeShape { Open, Half, Closed, Happy }

enum Legs { Stand, StepA, StepB, Tucked }

/// <summary>16×16 character grid; '.' is transparent, other characters map to palette colours.</summary>
sealed class Grid
{
    public const int Size = 16;
    private readonly char[,] _cells = new char[Size, Size];

    public Grid()
    {
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                _cells[y, x] = '.';
    }

    public char this[int x, int y] => _cells[y, x];

    public void Set(int x, int y, char c)
    {
        if (x is >= 0 and < Size && y is >= 0 and < Size) _cells[y, x] = c;
    }

    public void Row(int y, int fromX, int toX, char c)
    {
        for (var x = fromX; x <= toX; x++) Set(x, y, c);
    }

    public Grid Stamp(string[] art, int left, int top)
    {
        for (var y = 0; y < art.Length; y++)
            for (var x = 0; x < art[y].Length; x++)
                if (art[y][x] != '.') Set(left + x, top + y, art[y][x]);
        return this;
    }

    public Grid Shifted(int dx)
    {
        var copy = new Grid();
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (_cells[y, x] != '.') copy.Set(x + dx, y, _cells[y, x]);
        return copy;
    }
}

static class Frames
{
    private static readonly string[] Heart = [".P.P.", "PPPPP", ".PPP.", "..P.."];
    private static readonly string[] Z = ["ZZZ", ".Z.", "ZZZ"];
    private static readonly string[] Egg =
    [
        "................",
        "................",
        "................",
        "................",
        "................",
        "......EEEE......",
        ".....EEEEEE.....",
        "....EESEEEEE....",
        "....EEEEESEE....",
        "...EEEEEEEEEE...",
        "...EESEEEEEEE...",
        "...EEEEEESEEE...",
        "...EEEEEEEEEE...",
        "....EEESEEEE....",
        ".....EEEEEE.....",
        "......EEEE......",
    ];

    // ARGB. Body colour matches the reference critter's orange.
    private static readonly Dictionary<char, uint> Palette = new()
    {
        ['O'] = 0xFFD97757, // body
        ['D'] = 0xFFB25C40, // belly shade
        ['K'] = 0xFF1E1A1A, // eyes
        ['M'] = 0xFF6B2E1F, // mouth
        ['P'] = 0xFFFF5C8A, // heart
        ['Z'] = 0xFF8EC5FF, // sleep Z
        ['G'] = 0xFFBFBFBF, // thinking dots
        ['E'] = 0xFFF4E9D0, // egg shell
        ['S'] = 0xFFD8B98A, // egg spots
    };

    public static Grid[] Build()
    {
        var f = new Grid[32];
        f[0] = Creature(0, EyeShape.Open, 0, Legs.Stand);
        f[1] = Creature(1, EyeShape.Open, 0, Legs.Stand);
        f[2] = Creature(0, EyeShape.Closed, 0, Legs.Stand);
        f[3] = f[0];
        f[4] = Creature(0, EyeShape.Open, 1, Legs.StepA);
        f[5] = Creature(1, EyeShape.Open, 1, Legs.Stand);
        f[6] = Creature(0, EyeShape.Open, 1, Legs.StepB);
        f[7] = f[5];
        f[8] = Creature(0, EyeShape.Happy, 0, Legs.Stand).Stamp(Heart, 11, 0);
        f[9] = Creature(1, EyeShape.Happy, 0, Legs.Stand).Stamp(Heart, 11, 1);
        f[10] = Creature(-1, EyeShape.Happy, 0, Legs.Stand).Stamp(Heart, 11, 0);
        f[11] = f[8];
        f[12] = Creature(1, EyeShape.Half, -1, Legs.Stand);
        f[13] = Creature(1, EyeShape.Half, 1, Legs.Stand);
        f[14] = f[12];
        f[15] = f[13];
        f[16] = Creature(1, EyeShape.Half, 0, Legs.Stand);
        f[17] = Creature(1, EyeShape.Closed, 0, Legs.Stand, mouth: true);
        f[18] = f[16];
        f[19] = f[17];
        f[20] = Creature(0, EyeShape.Closed, 0, Legs.Tucked).Stamp(Z, 12, 3);
        f[21] = Creature(0, EyeShape.Closed, 0, Legs.Tucked).Stamp(Z, 12, 3).Stamp(Z, 13, 0);
        f[22] = f[20];
        f[23] = f[21];
        f[24] = Creature(0, EyeShape.Open, 1, Legs.Stand).Stamp(["G"], 6, 2);
        f[25] = Creature(0, EyeShape.Open, 1, Legs.Stand).Stamp(["G.G"], 6, 2);
        f[26] = Creature(0, EyeShape.Open, 1, Legs.Stand).Stamp(["G.G.G"], 6, 2);
        f[27] = f[24];
        f[28] = Creature(0, EyeShape.Open, 0, Legs.Stand, mouth: true);
        f[29] = f[0];
        f[30] = new Grid().Stamp(Egg, 0, 0);
        f[31] = f[30].Shifted(1);
        return f;
    }

    /// <param name="bob">Vertical offset of the body: -1 jump, 0 normal, 1 squat.</param>
    /// <param name="look">Horizontal eye offset: -1 left, 0 centre, 1 right.</param>
    private static Grid Creature(int bob, EyeShape eyes, int look, Legs legs, bool mouth = false)
    {
        var g = new Grid();
        var top = legs == Legs.Tucked ? 7 : 5 + bob;
        for (var y = top; y <= top + 3; y++) g.Row(y, 3, 12, 'O');
        g.Row(top + 4, 1, 14, 'O');
        g.Row(top + 5, 1, 14, 'O');
        g.Row(top + 6, 3, 12, 'O');
        g.Row(top + 7, 3, 12, 'D');

        var eyeY = top + 2;
        foreach (var eyeX in new[] { 5 + look, 10 + look })
        {
            switch (eyes)
            {
                case EyeShape.Open:
                    g.Set(eyeX, eyeY, 'K');
                    g.Set(eyeX, eyeY + 1, 'K');
                    break;
                case EyeShape.Half:
                    g.Set(eyeX, eyeY + 1, 'K');
                    break;
                case EyeShape.Closed:
                    g.Set(eyeX - 1, eyeY + 1, 'K');
                    g.Set(eyeX, eyeY + 1, 'K');
                    break;
                case EyeShape.Happy:
                    g.Set(eyeX, eyeY, 'K');
                    g.Set(eyeX - 1, eyeY + 1, 'K');
                    g.Set(eyeX + 1, eyeY + 1, 'K');
                    break;
            }
        }

        if (mouth)
        {
            g.Row(top + 4, 7, 8, 'M');
            g.Row(top + 5, 7, 8, 'M');
        }

        int[] legColumns = legs switch
        {
            Legs.Stand => [3, 5, 10, 12],
            Legs.StepA => [2, 5, 10, 13],
            Legs.StepB => [4, 6, 9, 11],
            _ => [],
        };
        for (var y = top + 8; y < Grid.Size; y++)
            foreach (var x in legColumns)
                g.Set(x, y, 'O');
        return g;
    }

    public static void WritePng(Grid[] frames, string path)
    {
        const int columns = 4;
        var rows = (frames.Length + columns - 1) / columns;
        var width = columns * Grid.Size;
        var height = rows * Grid.Size;
        var pixels = new uint[width * height];
        for (var i = 0; i < frames.Length; i++)
        {
            var originX = i % columns * Grid.Size;
            var originY = i / columns * Grid.Size;
            for (var y = 0; y < Grid.Size; y++)
                for (var x = 0; x < Grid.Size; x++)
                    if (Palette.TryGetValue(frames[i][x, y], out var argb))
                        pixels[(originY + y) * width + originX + x] = argb;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
