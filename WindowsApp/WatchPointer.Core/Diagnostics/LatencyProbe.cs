namespace WatchPointer.Core.Diagnostics;

/// <summary>
/// Round-trip measurement over the real BLE link (protocol v2): the PC sends a ping notification with a sequence
/// number, a v2 watch answers with a pong write. RTT = pong arrival − ping send, measured entirely on the PC clock,
/// so no clock synchronisation is involved. One-way latency is estimated as RTT / 2 (an assumption: the link may be
/// asymmetric, which is why the UI labels it "estimated").
/// </summary>
public sealed class LatencyProbe
{
    private readonly Dictionary<ushort, double> _outstanding = [];
    private readonly RollingWindow _rtt = new(128);
    private ushort _nextSequence = 1;
    private readonly object _gate = new();

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);
    public int Sent { get; private set; }
    public int Received { get; private set; }
    public int Lost { get; private set; }

    public ushort NextPing(double now)
    {
        lock (_gate)
        {
            ExpireOld(now);
            var seq = _nextSequence++;
            if (_nextSequence == 0) _nextSequence = 1;
            _outstanding[seq] = now;
            Sent++;
            return seq;
        }
    }

    /// <summary>Returns the RTT in seconds, or null for an unknown/expired sequence number.</summary>
    public double? OnPong(ushort sequence, double now)
    {
        lock (_gate)
        {
            if (!_outstanding.Remove(sequence, out var sentAt)) return null;
            var rtt = now - sentAt;
            if (rtt < 0 || rtt > Timeout.TotalSeconds) { Lost++; return null; }
            Received++;
            _rtt.Add(rtt);
            return rtt;
        }
    }

    public RollingWindow.Summary RoundTrip { get { lock (_gate) return _rtt.Summarize(); } }

    public void Reset()
    {
        lock (_gate) { _outstanding.Clear(); _rtt.Clear(); Sent = Received = Lost = 0; }
    }

    private void ExpireOld(double now)
    {
        List<ushort>? expired = null;
        foreach (var (seq, at) in _outstanding)
            if (now - at > Timeout.TotalSeconds) (expired ??= []).Add(seq);
        if (expired is null) return;
        foreach (var seq in expired) _outstanding.Remove(seq);
        Lost += expired.Count;
    }
}
