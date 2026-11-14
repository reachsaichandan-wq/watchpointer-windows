using WatchPointer.Core.Protocol;

namespace WatchPointer.Core.Simulation;

public enum SimulationPattern
{
    /// <summary>Aim follows <see cref="MotionSimulator.ManualLeft"/>/<see cref="MotionSimulator.ManualUp"/> (sliders/keys).</summary>
    Manual,
    Circle,
    FigureEight,
    /// <summary>Slow left-right sweeps across the screen.</summary>
    Sweep,
    /// <summary>Holding still with realistic hand tremor (tests the 1€ filter's steadiness).</summary>
    HoldStill,
    /// <summary>Jumps between targets (tests lag on fast moves).</summary>
    Steps,
}

/// <summary>
/// A virtual Apple Watch. Produces exactly the bytes the real watch writes (protocol v2), at 100 Hz, with the watch's
/// 16-bit millisecond timestamps, hand tremor noise and optionally Bluetooth-like burst delivery (samples queued and
/// delivered together every connection interval). Feeding its output into <c>PointerEngine.HandleRaw</c> exercises the
/// same decode → filter → project → cursor path as a real watch, so the engine can be tested without hardware.
/// </summary>
public sealed class MotionSimulator
{
    private readonly Random _random;
    private readonly Queue<(double Due, byte[] Packet)> _inFlight = new();
    private double _watchTime;
    private double _nextSample;
    private double _tremorX, _tremorY;

    public MotionSimulator(int seed = 1234) => _random = new Random(seed);

    public SimulationPattern Pattern { get; set; } = SimulationPattern.Circle;
    public double SampleRateHz { get; set; } = 100;
    /// <summary>Peak aim angle of the patterns, degrees.</summary>
    public double Amplitude { get; set; } = 18;
    /// <summary>Pattern speed, cycles per second.</summary>
    public double Frequency { get; set; } = 0.25;
    /// <summary>Tremor amplitude, degrees (≈ 0.05–0.2° for a steady hand).</summary>
    public double TremorDegrees { get; set; } = 0.12;
    /// <summary>BLE connection interval to emulate, seconds (0 = deliver each sample immediately).</summary>
    public double ConnectionInterval { get; set; } = 0.030;
    /// <summary>Extra random delivery delay, seconds.</summary>
    public double JitterSeconds { get; set; } = 0.004;
    /// <summary>Base one-way link latency, seconds.</summary>
    public double LinkLatency { get; set; } = 0.012;
    public double ManualLeft { get; set; }
    public double ManualUp { get; set; }
    public bool SendInfoOnStart { get; set; } = true;

    public double CurrentLeft { get; private set; }
    public double CurrentUp { get; private set; }

    private bool _started;
    private double _startTime;

    public void Reset()
    {
        _inFlight.Clear();
        _started = false;
    }

    /// <summary>Advances the virtual watch to PC time <paramref name="now"/> and returns packets that "arrive" by then.</summary>
    public IEnumerable<byte[]> Advance(double now)
    {
        if (!_started)
        {
            _started = true;
            _startTime = now;
            _nextSample = now;
            _watchTime = 3600 + _random.NextDouble() * 100; // watch clock has its own epoch
            if (SendInfoOnStart)
                _inFlight.Enqueue((now, PacketCodec.EncodeInfo(new WatchInfo
                {
                    Model = "Simulator", Os = "sim", Battery = 100, Name = "Simulated Watch", Proto = 2, AppVersion = "sim",
                })));
        }

        var interval = 1.0 / SampleRateHz;
        while (_nextSample <= now)
        {
            var t = _nextSample - _startTime;
            var (left, up) = Aim(t);
            _tremorX = 0.9 * _tremorX + 0.1 * Gaussian() * TremorDegrees * 3;
            _tremorY = 0.9 * _tremorY + 0.1 * Gaussian() * TremorDegrees * 3;
            CurrentLeft = left + _tremorX;
            CurrentUp = up + _tremorY;
            var ms = PacketCodec.ToWatchMs(_watchTime + t);
            var due = Deliver(_nextSample);
            _inFlight.Enqueue((due, PacketCodec.EncodeAim(CurrentLeft, CurrentUp, ms)));
            _nextSample += interval;
        }

        while (_inFlight.Count > 0 && _inFlight.Peek().Due <= now) yield return _inFlight.Dequeue().Packet;
    }

    /// <summary>Encodes a watch event (click, drag…) stamped with the current watch time.</summary>
    public byte[] Event(PacketKind kind, short a = 0, short b = 0, double ageSeconds = 0)
    {
        var t = (_nextSample - _startTime) - ageSeconds;
        return PacketCodec.Encode(kind, a, b, PacketCodec.ToWatchMs(_watchTime + t));
    }

    private double Deliver(double measuredAt)
    {
        var arrival = measuredAt + LinkLatency + _random.NextDouble() * JitterSeconds;
        if (ConnectionInterval > 0) arrival = Math.Ceiling(arrival / ConnectionInterval) * ConnectionInterval;
        // Delivery order is preserved (ATT writes are sequential).
        if (_inFlight.Count > 0) arrival = Math.Max(arrival, _inFlight.Last().Due);
        return arrival;
    }

    private (double Left, double Up) Aim(double t)
    {
        var w = 2 * Math.PI * Frequency * t;
        return Pattern switch
        {
            SimulationPattern.Manual => (ManualLeft, ManualUp),
            SimulationPattern.Circle => (Amplitude * Math.Cos(w), Amplitude * 0.6 * Math.Sin(w)),
            SimulationPattern.FigureEight => (Amplitude * Math.Sin(w), Amplitude * 0.5 * Math.Sin(2 * w)),
            SimulationPattern.Sweep => (Amplitude * Math.Sin(w), 0),
            SimulationPattern.HoldStill => (0, 0),
            SimulationPattern.Steps => (((int)(t * Frequency * 4) % 4) switch
            {
                0 => (-Amplitude, Amplitude * 0.5),
                1 => (Amplitude, Amplitude * 0.5),
                2 => (Amplitude, -Amplitude * 0.5),
                _ => (-Amplitude, -Amplitude * 0.5),
            }),
            _ => (0, 0),
        };
    }

    private double Gaussian()
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
