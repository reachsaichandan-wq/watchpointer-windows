using WatchPointer.Core.Calibration;
using WatchPointer.Core.Pointer;

namespace WatchPointer.Core.Tests;

public class AbsoluteProjectorTests
{
    private static readonly RectI Screen = RectI.FromSize(0, 0, 1920, 1080);
    private static readonly PointerSettings Defaults = new();

    [Fact]
    public void Center_aim_hits_center()
    {
        var p = AbsoluteProjector.Project(0, 0, Screen, Defaults);
        Assert.Equal(960, p.X, 6);
        Assert.Equal(540, p.Y, 6);
    }

    [Fact]
    public void Reach_angle_hits_the_edges()
    {
        // Turning right by 25° (left = -25) reaches the right edge at speed 1, like the original.
        Assert.Equal(1920, AbsoluteProjector.Project(-25, 0, Screen, Defaults).X, 6);
        Assert.Equal(0, AbsoluteProjector.Project(25, 0, Screen, Defaults).X, 6);
    }

    [Fact]
    public void Higher_speed_needs_less_turn()
    {
        var s = Defaults with { HorizontalSpeed = 2 };
        Assert.Equal(1920, AbsoluteProjector.Project(-12.5, 0, Screen, s).X, 6);
    }

    [Fact]
    public void Matches_the_tangent_law()
    {
        var d = AbsoluteProjector.EffectiveDistance(1920, 1);
        var p = AbsoluteProjector.Project(-10, 0, Screen, Defaults);
        Assert.Equal(960 + Math.Tan(10 * Math.PI / 180) * d, p.X, 6);
    }

    [Fact]
    public void Tipping_up_moves_up_and_includes_the_cosine_term()
    {
        var d = AbsoluteProjector.EffectiveDistance(1920, 1);
        var p = AbsoluteProjector.Project(-20, 5, Screen, Defaults);
        var h = 20 * Math.PI / 180;
        Assert.True(p.Y < 540);
        Assert.Equal(540 - Math.Tan(5 * Math.PI / 180) / Math.Cos(h) * d, p.Y, 6);
    }

    [Fact]
    public void Independent_vertical_speed()
    {
        var slow = AbsoluteProjector.Project(0, 5, Screen, Defaults);
        var fast = AbsoluteProjector.Project(0, 5, Screen, Defaults with { VerticalSpeed = 2 });
        Assert.True(540 - fast.Y > 1.9 * (540 - slow.Y));
        Assert.Equal(slow.X, fast.X, 6);
    }

    [Fact]
    public void Angles_are_clamped_so_tan_never_explodes()
    {
        var p = AbsoluteProjector.Project(-170, 89.9, Screen, Defaults);
        Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y));
        var limit = AbsoluteProjector.Project(-80, 0, Screen, Defaults);
        Assert.Equal(limit.X, p.X, 6);
    }

    [Fact]
    public void Inversion_flips_axes()
    {
        var s = Defaults with { InvertHorizontal = true, InvertVertical = true };
        var p = AbsoluteProjector.Project(-10, 5, Screen, s);
        Assert.True(p.X < 960 && p.Y > 540);
    }

    [Theory]
    [InlineData(-12.0, 4.0)]
    [InlineData(20.0, -7.5)]
    [InlineData(0.0, 0.0)]
    public void Unproject_inverts_project(double left, double up)
    {
        foreach (var s in new[] { Defaults, Defaults with { HorizontalSpeed = 1.7, VerticalSpeed = 0.6, InvertVertical = true } })
        {
            var p = AbsoluteProjector.Project(left, up, Screen, s);
            var (l, u) = AbsoluteProjector.Unproject(p, Screen, s);
            Assert.Equal(left, l, 6);
            Assert.Equal(up, u, 6);
        }
    }

    [Fact]
    public void Offset_pointing_area_is_respected()
    {
        var right = RectI.FromSize(1920, -200, 2560, 1440);
        var p = AbsoluteProjector.Project(0, 0, right, Defaults);
        Assert.Equal(1920 + 1280, p.X, 6);
        Assert.Equal(-200 + 720, p.Y, 6);
    }
}

public class RelativeMoverTests
{
    private static readonly PointerSettings S = new() { Mode = PointerMode.Relative, RelativeSpeed = 20, RelativeAcceleration = 0, RelativeDeadZoneDegPerSec = 2 };

    [Fact]
    public void First_sample_only_sets_the_reference()
    {
        var m = new RelativeMover();
        Assert.Equal((0.0, 0.0), m.Update(10, 10, 0.01, S));
    }

    [Fact]
    public void Turning_right_moves_right_and_tipping_up_moves_up()
    {
        var m = new RelativeMover();
        m.Update(0, 0, 0.01, S);
        var (dx, dy) = m.Update(-1, 0.5, 0.01, S); // 1° right, 0.5° up in 10 ms
        Assert.Equal(20, dx);
        Assert.Equal(-10, dy);
    }

    [Fact]
    public void Dead_zone_ignores_slow_drift()
    {
        var m = new RelativeMover();
        double left = 0;
        m.Update(left, 0, 0.01, S);
        double total = 0;
        for (var i = 0; i < 1000; i++) { left -= 0.01; total += m.Update(left, 0, 0.01, S).Dx; } // 1 °/s drift
        Assert.Equal(0, total);
    }

    [Fact]
    public void Subpixel_motion_accumulates()
    {
        var m = new RelativeMover();
        var s = S with { RelativeDeadZoneDegPerSec = 0 };
        double left = 0, total = 0;
        m.Update(left, 0, 0.01, s);
        for (var i = 0; i < 100; i++) { left -= 0.004; total += m.Update(left, 0, 0.01, s).Dx; } // 0.08 px per step
        Assert.InRange(total, 7, 8);
    }

    [Fact]
    public void Acceleration_makes_fast_turns_travel_further()
    {
        double Travel(double degPerStep)
        {
            var m = new RelativeMover();
            var s = S with { RelativeAcceleration = 1 };
            double left = 0, total = 0;
            m.Update(0, 0, 0.01, s);
            var steps = (int)(10 / degPerStep);
            for (var i = 0; i < steps; i++) { left -= degPerStep; total += m.Update(left, 0, 0.01, s).Dx; }
            return total; // same 10° total turn
        }
        Assert.True(Travel(1.0) > 1.5 * Travel(0.1));
    }

    [Fact]
    public void Heading_wrap_does_not_jump()
    {
        var m = new RelativeMover();
        m.Update(179.5, 0, 0.01, S);
        var (dx, _) = m.Update(-179.5, 0, 0.01, S); // turned 1° left across ±180
        Assert.Equal(-20, dx);
    }

    [Theory]
    [InlineData(190, -170)]
    [InlineData(-190, 170)]
    [InlineData(180, 180)]
    [InlineData(-180, 180)]
    [InlineData(725, 5)]
    public void Wrap_degrees(double input, double expected) => Assert.Equal(expected, RelativeMover.WrapDegrees(input), 9);
}

public class DisplayLayoutTests
{
    // Primary 1920×1080 at origin, a 2560×1440 monitor on the left (negative x) set 200 px higher, a gap-free layout.
    private static readonly DisplayLayout TwoMonitors = new([
        new DisplayInfo("A", "Primary", RectI.FromSize(0, 0, 1920, 1080), RectI.FromSize(0, 0, 1920, 1040), true, 1.25),
        new DisplayInfo("B", "Left", RectI.FromSize(-2560, -200, 2560, 1440), RectI.FromSize(-2560, -200, 2560, 1400), false, 1),
    ]);

    [Fact]
    public void Virtual_bounds_cover_all_monitors()
    {
        Assert.Equal(new RectI(-2560, -200, 1920, 1240), TwoMonitors.VirtualBounds);
        Assert.Equal("A", TwoMonitors.Primary.Id);
    }

    [Fact]
    public void Point_on_a_monitor_is_unchanged()
    {
        Assert.Equal(new PointD(-100, 1000), TwoMonitors.Clamp(new PointD(-100, 1000)));
    }

    [Fact]
    public void Gap_below_primary_snaps_to_nearest_monitor()
    {
        // (500, 1200) is below the primary (which ends at 1080) and right of the left monitor: nearest is the primary.
        var p = TwoMonitors.Clamp(new PointD(500, 1200));
        Assert.Equal(new PointD(500, 1079), p);
    }

    [Fact]
    public void Beyond_outer_edge_pins_to_edge()
    {
        Assert.Equal(new PointD(1919, 300), TwoMonitors.Clamp(new PointD(5000, 300)));
        Assert.Equal(new PointD(-2560, -200), TwoMonitors.Clamp(new PointD(-9000, -9000)));
    }

    [Fact]
    public void Pointing_area_selection()
    {
        Assert.Equal(TwoMonitors.Primary.Bounds, TwoMonitors.PointingArea(PointingArea.PrimaryDisplay, null));
        Assert.Equal(TwoMonitors.VirtualBounds, TwoMonitors.PointingArea(PointingArea.AllDisplays, null));
        Assert.Equal(TwoMonitors.Displays[1].Bounds, TwoMonitors.PointingArea(PointingArea.SpecificDisplay, "B"));
        Assert.Equal(TwoMonitors.Primary.Bounds, TwoMonitors.PointingArea(PointingArea.SpecificDisplay, "missing"));
    }

    [Fact]
    public void Empty_layout_falls_back_to_a_default_display()
    {
        var l = new DisplayLayout([]);
        Assert.Single(l.Displays);
        Assert.False(l.VirtualBounds.IsEmpty);
    }
}

public class AbsoluteMouseCoordinatesTests
{
    [Theory]
    [InlineData(0, 1920)]
    [InlineData(-2560, 4480)]
    [InlineData(-1000, 7680)]
    [InlineData(0, 1366)]
    public void Every_pixel_roundtrips_exactly(int origin, int size)
    {
        for (var x = origin; x < origin + size; x++)
        {
            var n = AbsoluteMouseCoordinates.Normalize(x, origin, size);
            Assert.InRange(n, 0, 65535);
            Assert.Equal(x, AbsoluteMouseCoordinates.Denormalize(n, origin, size));
        }
    }

    [Fact]
    public void Out_of_range_pixels_are_clamped()
    {
        Assert.Equal(AbsoluteMouseCoordinates.Normalize(0, 0, 100), AbsoluteMouseCoordinates.Normalize(-50, 0, 100));
        Assert.Equal(AbsoluteMouseCoordinates.Normalize(99, 0, 100), AbsoluteMouseCoordinates.Normalize(500, 0, 100));
    }
}

public class CalibrationTests
{
    private static readonly RectI Screen = RectI.FromSize(0, 0, 1920, 1080);

    [Fact]
    public void Solves_speeds_that_put_the_measured_aims_on_the_edges()
    {
        var center = (Left: 3.0, Up: -2.0);
        var right = (Left: 3.0 - 20, Up: -2.0);
        var top = (Left: 3.0, Up: -2.0 + 9);
        var r = CalibrationCalculator.Solve(center, right, top, Screen);
        Assert.NotNull(r);
        var s = new PointerSettings { HorizontalSpeed = r.HorizontalSpeed, VerticalSpeed = r.VerticalSpeed };
        Assert.Equal(1920, AbsoluteProjector.Project(right.Left - center.Left, 0, Screen, s).X, 3);
        Assert.Equal(0, AbsoluteProjector.Project(0, top.Up - center.Up, Screen, s).Y, 3);
    }

    [Fact]
    public void Rejects_too_small_movements()
    {
        Assert.Null(CalibrationCalculator.Solve((0, 0), (-0.5, 0), (0, 10), Screen));
    }

    [Fact]
    public void Average_handles_heading_wrap()
    {
        var (l, u) = CalibrationCalculator.Average([(179, 1), (-179, 3)]);
        Assert.Equal(180, Math.Abs(l), 6);
        Assert.Equal(2, u, 6);
        Assert.True(CalibrationCalculator.Spread([(179, 1), (-179, 3)]) < 2);
    }
}
