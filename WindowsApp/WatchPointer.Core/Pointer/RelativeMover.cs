namespace WatchPointer.Core.Pointer;

/// <summary>
/// Air-mouse mode: turning the wrist moves the cursor by an amount proportional to the turn, with optional
/// acceleration and a dead zone on angular speed. Sub-pixel remainders are carried over so slow turns still add up.
/// Because only changes in aim are used, slow heading drift on the watch translates into at most a slow cursor creep,
/// which the dead zone removes entirely.
/// </summary>
public sealed class RelativeMover
{
    private double? _lastLeft, _lastUp;
    private double _remainderX, _remainderY;

    /// <summary>Returns the cursor movement in pixels for the new (smoothed) aim sample.</summary>
    public (double Dx, double Dy) Update(double leftDegrees, double upDegrees, double dt, PointerSettings s)
    {
        if (_lastLeft is not { } lastLeft || _lastUp is not { } lastUp || !(dt > 0))
        {
            _lastLeft = leftDegrees;
            _lastUp = upDegrees;
            return (0, 0);
        }

        var dl = WrapDegrees(leftDegrees - lastLeft);
        var du = upDegrees - lastUp;
        _lastLeft = leftDegrees;
        _lastUp = upDegrees;

        var angularSpeed = Math.Sqrt(dl * dl + du * du) / dt; // degrees per second
        if (angularSpeed < s.RelativeDeadZoneDegPerSec) return (0, 0);

        // Gain grows linearly with speed above the dead zone: k · (1 + a · (ω − dz) / 100 °/s).
        var gain = s.RelativeSpeed * (1 + s.RelativeAcceleration * (angularSpeed - s.RelativeDeadZoneDegPerSec) / 100.0);
        var dx = (s.InvertHorizontal ? dl : -dl) * gain + _remainderX;
        var dy = (s.InvertVertical ? du : -du) * gain + _remainderY;
        var wholeX = Math.Truncate(dx);
        var wholeY = Math.Truncate(dy);
        _remainderX = dx - wholeX;
        _remainderY = dy - wholeY;
        return (wholeX, wholeY);
    }

    public void Reset()
    {
        _lastLeft = _lastUp = null;
        _remainderX = _remainderY = 0;
    }

    /// <summary>Maps an angle difference into (−180, 180] so a heading that wraps around doesn't jump the cursor.</summary>
    public static double WrapDegrees(double d)
    {
        d %= 360;
        if (d > 180) d -= 360;
        else if (d <= -180) d += 360;
        return d;
    }
}
