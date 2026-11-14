namespace WatchPointer.Core.Motion;

/// <summary>
/// Turns the watch's wrapping 16-bit millisecond timestamps into a continuous clock and lines it up with the PC clock.
/// <para>The offset (PC time − watch time) is taken from the sample that arrived quickest, i.e. with the least Bluetooth
/// delay, exactly as the original receiver does. Improvement: the minimum is allowed to creep upward slowly
/// (<see cref="OffsetDriftPerSecond"/>) so crystal drift between the two clocks can't leave it stale forever.</para>
/// </summary>
public sealed class WatchClock
{
    private ushort? _lastMs;
    private double _lastOffsetUpdate;

    /// <summary>How fast the minimum offset may relax upward, seconds per second (default 50 ppm ≈ 3 ms per minute).</summary>
    public double OffsetDriftPerSecond { get; set; } = 50e-6;

    /// <summary>Unwrapped watch time of the newest sample, in seconds.</summary>
    public double WatchTime { get; private set; }

    /// <summary>PC time minus watch time, from the quickest-arriving sample; null until a timed sample arrives.</summary>
    public double? Offset { get; private set; }

    public ushort? LastWatchMs => _lastMs;

    /// <summary>True when <paramref name="watchMs"/> is older than the newest sample (delivered out of order).
    /// Uses the signed 16-bit difference, so wrap-around is handled.</summary>
    public bool IsStale(ushort watchMs) => _lastMs is { } last && unchecked((short)(watchMs - last)) < 0;

    /// <summary>Registers a timed sample. Returns the watch-measured time since the previous sample (seconds),
    /// or null for the first sample after <see cref="Reset"/>.</summary>
    public double? Observe(ushort watchMs, double pcArrival, bool fresh)
    {
        double? dt = null;
        if (_lastMs is { } previous && !fresh)
        {
            dt = unchecked((ushort)(watchMs - previous)) / 1000.0;
            WatchTime += dt.Value;
        }
        else
        {
            WatchTime = watchMs / 1000.0;
            Offset = null;
        }
        _lastMs = watchMs;

        var candidate = pcArrival - WatchTime;
        if (Offset is { } offset)
        {
            var relaxed = offset + Math.Max(0, pcArrival - _lastOffsetUpdate) * OffsetDriftPerSecond;
            Offset = Math.Min(relaxed, candidate);
        }
        else
        {
            Offset = candidate;
        }
        _lastOffsetUpdate = pcArrival;
        return dt;
    }

    /// <summary>Delay of the newest sample beyond the quickest one seen (≥ 0), in seconds: Bluetooth queueing/jitter.</summary>
    public double ExcessDelay(double pcArrival) => Offset is { } o ? Math.Max(0, pcArrival - WatchTime - o) : 0;

    /// <summary>PC time that a click/drag timestamp refers to. The stamp may be slightly before or after the
    /// newest aim sample (signed 16-bit difference). Null when the clock isn't synchronised yet.</summary>
    public double? ToPcTime(ushort eventMs)
    {
        if (_lastMs is not { } last || Offset is not { } offset) return null;
        var sinceLastSample = unchecked((short)(eventMs - last)) / 1000.0;
        return WatchTime + sinceLastSample + offset;
    }

    public void Reset()
    {
        _lastMs = null;
        Offset = null;
        WatchTime = 0;
    }
}
