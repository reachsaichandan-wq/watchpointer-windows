using WatchPointer.Core.Pointer;

namespace WatchPointer.Core.Calibration;

/// <summary>
/// Guided calibration: the user points at the middle, then at the right edge, then at the top edge of the pointing
/// area. From the measured angles we solve the projection for the speeds that put those exact aims on those edges:
/// <code>
/// right edge: W/2 = tan(Δh) · Dx,  Dx = (W/2) / tan(25°/sx)        ⇒ sx = 25° / Δh
/// top edge:   H/2 = tan(Δv) · Dy,  Dy = (W/2) / tan(25°/sy)        ⇒ sy = 25° / atan((W/H) · tan(Δv))
/// </code>
/// </summary>
public static class CalibrationCalculator
{
    public const double MinUsefulAngle = 2.0;

    public sealed record Result(double HorizontalSpeed, double VerticalSpeed, double HorizontalReach, double VerticalReach);

    /// <param name="center">Raw aim (left, up) when pointing at the middle.</param>
    /// <param name="rightEdge">Raw aim when pointing at the right edge.</param>
    /// <param name="topEdge">Raw aim when pointing at the top edge.</param>
    public static Result? Solve((double Left, double Up) center, (double Left, double Up) rightEdge, (double Left, double Up) topEdge, RectI area)
    {
        if (area.IsEmpty) return null;
        var dh = Math.Abs(rightEdge.Left - center.Left); // turning right reduces "left"
        var dv = Math.Abs(topEdge.Up - center.Up);
        if (dh < MinUsefulAngle || dv < MinUsefulAngle || dh > 80 || dv > 80) return null;

        var sx = PointerSettings.BaseReachDegrees / dh;
        var vReach = Math.Atan((double)area.Width / area.Height * Math.Tan(AbsoluteProjector.DegToRad(dv)));
        var sy = PointerSettings.BaseReachDegrees / AbsoluteProjector.RadToDeg(vReach);
        return new Result(Math.Clamp(sx, 0.1, 10), Math.Clamp(sy, 0.1, 10), dh, dv);
    }

    /// <summary>Averages raw aims captured over a short hold, so one shaky sample doesn't decide the calibration.</summary>
    public static (double Left, double Up) Average(IReadOnlyList<(double Left, double Up)> samples)
    {
        if (samples.Count == 0) return (0, 0);
        // Heading may wrap at ±180°: average around the first sample.
        var reference = samples[0].Left;
        double sumL = 0, sumU = 0;
        foreach (var (l, u) in samples) { sumL += RelativeMover.WrapDegrees(l - reference); sumU += u; }
        return (reference + sumL / samples.Count, sumU / samples.Count);
    }

    /// <summary>Spread of the samples (max distance from the mean, degrees). Large = the hand wasn't still.</summary>
    public static double Spread(IReadOnlyList<(double Left, double Up)> samples)
    {
        if (samples.Count == 0) return 0;
        var (ml, mu) = Average(samples);
        return samples.Max(s => Math.Sqrt(Math.Pow(RelativeMover.WrapDegrees(s.Left - ml), 2) + Math.Pow(s.Up - mu, 2)));
    }
}
