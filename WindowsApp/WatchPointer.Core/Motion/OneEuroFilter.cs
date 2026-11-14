namespace WatchPointer.Core.Motion;

/// <summary>
/// 1€ filter (Casiez, Roussel, Vogel, CHI 2012): heavy smoothing when the hand is nearly still (no jitter),
/// almost none when it moves fast (no lag). Needs the real time between samples; the receiver feeds it the
/// watch's own measurement timestamps because Bluetooth delivers samples in bursts.
/// </summary>
public sealed class OneEuroFilter
{
    private double? _value;
    private double _velocity;

    public OneEuroFilter(double minCutoff = 0.8, double beta = 0.01, double derivativeCutoff = 1.0)
    {
        MinCutoff = minCutoff;
        Beta = beta;
        DerivativeCutoff = derivativeCutoff;
    }

    /// <summary>Smoothing when still, in Hz. Lower = steadier but slow moves trail a little. ("steady" in the original.)</summary>
    public double MinCutoff { get; set; }

    /// <summary>How quickly smoothing gets out of the way as speed rises. Higher = less lag, a bit more shake. ("quick".)</summary>
    public double Beta { get; set; }

    public double DerivativeCutoff { get; set; }

    public bool HasValue => _value.HasValue;

    public double Value => _value ?? 0;

    /// <summary>Filters <paramref name="x"/>. <paramref name="dt"/> is seconds since the previous sample. As in the
    /// original, the first sample, or a non-positive / non-finite dt, passes the value through unfiltered and makes
    /// it the new reference.</summary>
    public double Filter(double x, double dt)
    {
        if (_value is not { } previous || !(dt > 0) || !double.IsFinite(dt))
        {
            _value = x;
            return x;
        }

        var derivativeAlpha = Alpha(DerivativeCutoff, dt);
        _velocity += derivativeAlpha * ((x - previous) / dt - _velocity);
        var cutoff = MinCutoff + Beta * Math.Abs(_velocity);
        var smoothed = previous + Alpha(cutoff, dt) * (x - previous);
        _value = smoothed;
        return smoothed;
    }

    public void Reset()
    {
        _value = null;
        _velocity = 0;
    }

    private static double Alpha(double cutoff, double dt) => 1.0 / (1.0 + 1.0 / (2 * Math.PI * cutoff * dt));
}
