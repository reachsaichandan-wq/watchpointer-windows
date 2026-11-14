namespace WatchPointer.Core.Pointer;

/// <summary>
/// Laser-pointer projection: the aim hits a flat wall the size of the pointing area, like the original receiver.
/// <code>
/// h = −left (clamped to ±MaxAngle), v = up (clamped)
/// x = centerX + tan(h) · Dx            Dx = (width / 2) / tan(25° / horizontalSpeed)
/// y = centerY − tan(v) / cos(h) · Dy   Dy = (width / 2) / tan(25° / verticalSpeed)
/// </code>
/// D is the "effective distance" to the wall in pixels. The 1/cos(h) term keeps a constant elevation on a straight
/// horizontal line of the wall (it is the true ray/plane intersection, not an approximation).
/// </summary>
public static class AbsoluteProjector
{
    public static double EffectiveDistance(double areaWidth, double speed) =>
        areaWidth / 2.0 / Math.Tan(DegToRad(PointerSettings.BaseReachDegrees / speed));

    public static PointD Project(double leftDegrees, double upDegrees, RectI area, PointerSettings s)
    {
        var h = DegToRad(Math.Clamp(s.InvertHorizontal ? leftDegrees : -leftDegrees, -s.MaxAngleDegrees, s.MaxAngleDegrees));
        var v = DegToRad(Math.Clamp(s.InvertVertical ? -upDegrees : upDegrees, -s.MaxAngleDegrees, s.MaxAngleDegrees));
        var dx = EffectiveDistance(area.Width, s.HorizontalSpeed);
        var dy = EffectiveDistance(area.Width, s.VerticalSpeed);
        return new PointD(area.CenterX + Math.Tan(h) * dx, area.CenterY - Math.Tan(v) / Math.Cos(h) * dy);
    }

    /// <summary>Inverse of <see cref="Project"/>: the (left, up) aim that lands on <paramref name="p"/>.</summary>
    public static (double LeftDegrees, double UpDegrees) Unproject(PointD p, RectI area, PointerSettings s)
    {
        var dx = EffectiveDistance(area.Width, s.HorizontalSpeed);
        var dy = EffectiveDistance(area.Width, s.VerticalSpeed);
        var h = Math.Atan((p.X - area.CenterX) / dx);
        var v = Math.Atan((area.CenterY - p.Y) * Math.Cos(h) / dy);
        var left = s.InvertHorizontal ? RadToDeg(h) : -RadToDeg(h);
        var up = s.InvertVertical ? -RadToDeg(v) : RadToDeg(v);
        return (left, up);
    }

    public static double DegToRad(double d) => d * Math.PI / 180.0;
    public static double RadToDeg(double r) => r * 180.0 / Math.PI;
}
