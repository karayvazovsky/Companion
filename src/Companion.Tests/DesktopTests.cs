using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class DesktopTests
{
    private const double Size = 64;
    private static readonly ScreenArea Primary = new(new RectD(0, 0, 1920, 1080), new RectD(0, 0, 1920, 1040));
    private static readonly ScreenArea Second = new(new RectD(1920, 0, 1280, 1024), new RectD(1920, 0, 1280, 1024));
    private static readonly ScreenArea[] Screens = [Primary, Second];
    private static readonly DesktopWindow Editor = new(42, new RectD(500, 300, 800, 600));

    [Fact]
    public void Drop_near_edges_snaps_to_floor_walls_and_ceiling()
    {
        Assert.Equal(new Perch(PerchKind.Floor, 900, 976), PerchPlanner.ForDrop(900, 1040 - 64 - 10, Size, Screens, []));
        Assert.Equal(new Perch(PerchKind.WallLeft, 0, 500), PerchPlanner.ForDrop(10, 500, Size, Screens, []));
        Assert.Equal(new Perch(PerchKind.WallRight, 3136, 400), PerchPlanner.ForDrop(3200 - 64 - 5, 400, Size, Screens, []));
        Assert.Equal(new Perch(PerchKind.Ceiling, 700, 0), PerchPlanner.ForDrop(700, 8, Size, Screens, []));
    }

    [Fact]
    public void Drop_on_a_window_top_perches_there_with_an_offset()
    {
        var perch = PerchPlanner.ForDrop(700, 300 - 64 + 10, Size, Screens, [Editor]);
        Assert.Equal(new Perch(PerchKind.WindowTop, 700, 236, 42, 200), perch);
        Assert.True(perch.CanWalk);
    }

    [Fact]
    public void Drop_in_open_space_stays_free_and_clamped()
    {
        Assert.Equal(new Perch(PerchKind.Free, 900, 500), PerchPlanner.ForDrop(900, 500, Size, Screens, [Editor]));
        Assert.Equal(new Perch(PerchKind.WallLeft, 0, 500), PerchPlanner.ForDrop(-5000, 500, Size, Screens, []));
        Assert.False(new Perch(PerchKind.Free, 0, 0).CanWalk);
    }

    [Fact]
    public void A_window_touching_the_top_of_the_screen_is_not_perchable()
    {
        var maximized = new DesktopWindow(7, new RectD(0, 0, 1920, 1040));
        Assert.Equal(PerchKind.Ceiling, PerchPlanner.ForDrop(700, 10, Size, Screens, [maximized]).Kind);
    }

    [Fact]
    public void Follow_tracks_the_window_and_gives_up_when_it_is_gone()
    {
        var perch = new Perch(PerchKind.WindowTop, 700, 236, 42, 200);
        var moved = new DesktopWindow(42, new RectD(600, 400, 800, 600));
        Assert.Equal(new Perch(PerchKind.WindowTop, 800, 336, 42, 200), PerchPlanner.Follow(perch, Size, Screens, [moved]));
        Assert.Null(PerchPlanner.Follow(perch, Size, Screens, []));
        Assert.Null(PerchPlanner.Follow(perch, Size, Screens, [new DesktopWindow(42, new RectD(600, 400, 60, 600))]));
        var floor = new Perch(PerchKind.Floor, 10, 976);
        Assert.Same(floor, PerchPlanner.Follow(floor, Size, Screens, []));
    }

    [Fact]
    public void Fall_lands_on_the_floor_of_the_screen_below()
    {
        Assert.Equal(new Perch(PerchKind.Floor, 2500, 960), PerchPlanner.Fall(2500, Size, 100, Screens));
        Assert.Equal(new Perch(PerchKind.Floor, 1856, 976), PerchPlanner.Fall(1900, Size, 100, [Primary]));
    }

    [Fact]
    public void Walk_range_follows_the_perch()
    {
        Assert.Equal((0d, 1856d), PerchPlanner.WalkRange(new Perch(PerchKind.Floor, 5, 976), Size, Screens, []));
        Assert.Equal((500d, 1236d), PerchPlanner.WalkRange(new Perch(PerchKind.WindowTop, 700, 236, 42, 200), Size, Screens, [Editor]));
    }

    [Fact]
    public void Random_perches_stay_on_screen_and_use_every_kind()
    {
        var rng = new Random(3);
        var kinds = new HashSet<PerchKind>();
        for (var i = 0; i < 400; i++)
        {
            var perch = PerchPlanner.Random(rng, Size, Screens, [Editor]);
            kinds.Add(perch.Kind);
            var wa = PerchPlanner.ScreenFor(perch.X + Size / 2, perch.Y + Size / 2, Screens).WorkArea;
            Assert.InRange(perch.X, wa.Left - 0.001, wa.Right - Size + 0.001);
            Assert.InRange(perch.Y, wa.Top - 0.001, wa.Bottom - Size + 0.001);
        }
        Assert.Equal(new[] { PerchKind.Floor, PerchKind.WindowTop, PerchKind.WallLeft, PerchKind.WallRight, PerchKind.Ceiling }.ToHashSet(), kinds);
        for (var i = 0; i < 100; i++) Assert.NotEqual(PerchKind.WindowTop, PerchPlanner.Random(rng, Size, Screens, []).Kind);
    }

    [Fact]
    public void Pendulum_lags_behind_motion_clamps_and_settles()
    {
        var pendulum = new Pendulum();
        for (var i = 0; i < 30; i++) pendulum.Step(1 / 30.0, 1000);
        Assert.True(pendulum.Angle > 5);
        for (var i = 0; i < 30; i++) pendulum.Step(1 / 30.0, 1_000_000);
        Assert.True(pendulum.Angle <= Pendulum.MaxAngle);
        for (var i = 0; i < 120; i++) pendulum.Step(1 / 30.0, 0);
        Assert.InRange(pendulum.Angle, -0.5, 0.5);
        for (var i = 0; i < 30; i++) pendulum.Step(1 / 30.0, -1000);
        Assert.True(pendulum.Angle < -5);
        pendulum.Reset();
        Assert.Equal(0, pendulum.Angle);
        Assert.Equal(6, Pendulum.IdleSwing(0.5), 3);
    }
}
