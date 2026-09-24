namespace Companion.Core;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    public bool Contains(double px, double py) => px >= Left && px < Right && py >= Top && py < Bottom;
}

public sealed record ScreenArea(RectD Bounds, RectD WorkArea);

public sealed record DesktopWindow(long Handle, RectD Bounds);

public enum PerchKind { Floor, WindowTop, WallLeft, WallRight, Ceiling, Free }

/// <summary>Where the pet is. X/Y = top-left of the sprite square in DIPs; OffsetX = distance from the window's left edge (WindowTop).</summary>
public sealed record Perch(PerchKind Kind, double X, double Y, long WindowHandle = 0, double OffsetX = 0)
{
    public bool CanWalk => Kind is PerchKind.Floor or PerchKind.WindowTop;
}

/// <summary>Pure placement rules: snapping a drop, teleport targets, following windows, falling, walking ranges.</summary>
public static class PerchPlanner
{
    public const double SnapDistance = 24;
    private const double MinWindowWidthInPets = 1.5;

    public static ScreenArea ScreenFor(double x, double y, IReadOnlyList<ScreenArea> screens)
    {
        foreach (var screen in screens)
            if (screen.Bounds.Contains(x, y)) return screen;
        return screens.MinBy(s => Distance(s.Bounds, x, y))!;
    }

    public static Perch ForDrop(double x, double y, double size, IReadOnlyList<ScreenArea> screens, IReadOnlyList<DesktopWindow> windows)
    {
        var wa = ScreenFor(x + size / 2, y + size / 2, screens).WorkArea;
        x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - size));
        y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - size));
        var best = new Perch(PerchKind.Free, x, y);
        var bestDistance = double.MaxValue;

        void Consider(double distance, Perch perch)
        {
            if (distance <= SnapDistance && distance < bestDistance)
            {
                bestDistance = distance;
                best = perch;
            }
        }

        // Windows arrive topmost first. Walk down the pet's centre column: the first perchable top within snap distance
        // wins; a window covering the drop point (bottom centre) hides everything behind it.
        var center = x + size / 2;
        var bottom = y + size;
        foreach (var window in windows)
        {
            var b = window.Bounds;
            if (center < b.Left || center > b.Right) continue;
            if (Perchable(window, wa, size) && Math.Abs(bottom - b.Top) <= SnapDistance)
            {
                var px = Math.Clamp(x, b.Left, b.Right - size);
                Consider(Math.Abs(bottom - b.Top), new Perch(PerchKind.WindowTop, px, b.Top - size, window.Handle, px - b.Left));
                break;
            }
            if (b.Contains(center, bottom)) break;
        }
        Consider(wa.Bottom - (y + size), new Perch(PerchKind.Floor, x, wa.Bottom - size));
        Consider(x - wa.Left, new Perch(PerchKind.WallLeft, wa.Left, y));
        Consider(wa.Right - (x + size), new Perch(PerchKind.WallRight, wa.Right - size, y));
        Consider(y - wa.Top, new Perch(PerchKind.Ceiling, x, wa.Top));
        return best;
    }

    public static Perch Random(Random rng, double size, IReadOnlyList<ScreenArea> screens, IReadOnlyList<DesktopWindow> windows)
    {
        var wa = screens[rng.Next(screens.Count)].WorkArea;
        double RandomX() => wa.Left + rng.NextDouble() * Math.Max(0, wa.Width - size);
        double RandomY() => wa.Top + rng.NextDouble() * Math.Max(0, wa.Height - size);
        var tops = windows.Where(w => Perchable(w, wa, size) && w.Bounds.Left >= wa.Left - 1 && w.Bounds.Right <= wa.Right + 1).ToList();

        var roll = rng.NextDouble();
        if (roll < 0.25 && tops.Count > 0)
        {
            var w = tops[rng.Next(tops.Count)];
            var px = w.Bounds.Left + rng.NextDouble() * (w.Bounds.Width - size);
            return new Perch(PerchKind.WindowTop, px, w.Bounds.Top - size, w.Handle, px - w.Bounds.Left);
        }
        if (roll < 0.40) return new Perch(PerchKind.Ceiling, RandomX(), wa.Top);
        if (roll < 0.50) return new Perch(PerchKind.WallLeft, wa.Left, RandomY());
        if (roll < 0.60) return new Perch(PerchKind.WallRight, wa.Right - size, RandomY());
        return new Perch(PerchKind.Floor, RandomX(), wa.Bottom - size);
    }

    /// <summary>Updated WindowTop perch, the same perch for other kinds, or null when the window is gone or unusable.</summary>
    public static Perch? Follow(Perch perch, double size, IReadOnlyList<ScreenArea> screens, IReadOnlyList<DesktopWindow> windows)
    {
        if (perch.Kind != PerchKind.WindowTop) return perch;
        var window = windows.FirstOrDefault(w => w.Handle == perch.WindowHandle);
        if (window is null) return null;
        var b = window.Bounds;
        if (!Perchable(window, ScreenFor(b.CenterX, b.Top, screens).WorkArea, size)) return null;
        var px = Math.Clamp(b.Left + perch.OffsetX, b.Left, b.Right - size);
        return perch with { X = px, Y = b.Top - size, OffsetX = px - b.Left };
    }

    public static Perch Fall(double x, double size, double fromY, IReadOnlyList<ScreenArea> screens)
    {
        var wa = ScreenFor(x + size / 2, fromY + size / 2, screens).WorkArea;
        return new Perch(PerchKind.Floor, Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - size)), wa.Bottom - size);
    }

    public static (double Min, double Max) WalkRange(Perch perch, double size, IReadOnlyList<ScreenArea> screens, IReadOnlyList<DesktopWindow> windows)
    {
        if (perch.Kind == PerchKind.WindowTop && windows.FirstOrDefault(w => w.Handle == perch.WindowHandle) is { } window)
            return (window.Bounds.Left, window.Bounds.Right - size);
        var wa = ScreenFor(perch.X + size / 2, perch.Y + size / 2, screens).WorkArea;
        return (wa.Left, Math.Max(wa.Left, wa.Right - size));
    }

    private static bool Perchable(DesktopWindow window, RectD workArea, double size) =>
        window.Bounds.Width >= size * MinWindowWidthInPets
        && window.Bounds.Top - size >= workArea.Top
        && window.Bounds.Top <= workArea.Bottom;

    private static double Distance(RectD r, double x, double y)
    {
        var dx = Math.Max(Math.Max(r.Left - x, 0), x - r.Right);
        var dy = Math.Max(Math.Max(r.Top - y, 0), y - r.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
