using WatchPointer.Core.Diagnostics;
using WatchPointer.Core.Input;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Protocol;
using WatchPointer.Core.Session;

namespace WatchPointer.Core.Simulation;

/// <summary>
/// Measures how closely the cursor follows the hand, in virtual time: a simulated watch (100 Hz, tremor, Bluetooth
/// latency and burst delivery) drives the real engine at 240 Hz, and every tick the cursor is compared with where the
/// hand is pointing <em>right now</em> (so Bluetooth delay counts as error, exactly as the user perceives it).
/// </summary>
public static class TrackingBenchmark
{
    public sealed record Result(
        double MeanErrorPx, double P95ErrorPx, double LagMs, double StillSpreadPx, double OvershootPx)
    {
        public override string ToString() =>
            $"error mean {MeanErrorPx,5:F1} px · p95 {P95ErrorPx,5:F1} px · lag ≈ {LagMs,4:F0} ms · still spread {StillSpreadPx,4:F1} px · overshoot {OvershootPx,4:F1} px";
    }

    public static Result Run(PointerSettings settings, int seed = 7, double linkLatency = 0.012, double connectionInterval = 0.030)
    {
        var (meanError, p95, lag) = Track(settings, SimulationPattern.FigureEight, 10, seed, linkLatency, connectionInterval);
        var still = StillSpread(settings, seed, linkLatency, connectionInterval);
        var overshoot = Overshoot(settings, seed, linkLatency, connectionInterval);
        return new Result(meanError, p95, lag, still, overshoot);
    }

    private sealed class Rig
    {
        public readonly ManualClock Clock = new();
        public readonly NullInputSink Sink = new();
        public readonly PointerEngine Engine;
        public readonly MotionSimulator Sim;
        public readonly RectI Area = RectI.FromSize(0, 0, 1920, 1080);
        public readonly PointerSettings Settings;

        public Rig(PointerSettings settings, SimulationPattern pattern, int seed, double latency, double interval)
        {
            Settings = settings;
            Engine = new PointerEngine(Sink, new FixedLayout(DisplayLayout.Single(1920, 1080)), Clock) { Settings = settings };
            Sim = new MotionSimulator(seed) { Pattern = pattern, LinkLatency = latency, ConnectionInterval = interval, TremorDegrees = 0.1 };
        }

        private double _nextPing = 1000.5;
        private readonly Queue<(double Due, ushort Seq)> _pongs = new();

        /// <summary>Advances one cursor frame; returns (true aim point now, cursor).</summary>
        public (PointD Truth, PointD Cursor) Step()
        {
            foreach (var packet in Sim.Advance(Clock.Now)) Engine.HandleRaw(packet, Clock.Now);
            // v2 latency probe: ping notification out, pong write back one connection event later, each way delayed.
            if (Clock.Now >= _nextPing)
            {
                _nextPing += 0.5;
                var seq = Engine.Probe.NextPing(Clock.Now);
                var interval = Sim.ConnectionInterval;
                _pongs.Enqueue((Clock.Now + 2 * Sim.LinkLatency + interval / 2, seq));
            }
            while (_pongs.Count > 0 && _pongs.Peek().Due <= Clock.Now)
                Engine.HandleRaw(PacketCodec.Encode(PacketKind.Pong, unchecked((short)_pongs.Dequeue().Seq)), Clock.Now);
            Engine.Tick();
            var truth = AbsoluteProjector.Project(Sim.CurrentLeft, Sim.CurrentUp, Area, Settings);
            var c = Sink.CursorPosition;
            Clock.Advance(1.0 / 240);
            return (truth, new PointD(c.X, c.Y));
        }
    }

    private static (double Mean, double P95, double LagMs) Track(PointerSettings s, SimulationPattern pattern, double seconds, int seed, double latency, double interval)
    {
        var rig = new Rig(s, pattern, seed, latency, interval);
        var errors = new List<double>();
        var truths = new List<PointD>();
        var cursors = new List<PointD>();
        for (var t = 0.0; t < seconds; t += 1.0 / 240)
        {
            var (truth, cursor) = rig.Step();
            if (t < 1) continue; // let it settle
            errors.Add(Math.Sqrt(Math.Pow(truth.X - cursor.X, 2) + Math.Pow(truth.Y - cursor.Y, 2)));
            truths.Add(truth);
            cursors.Add(cursor);
        }
        errors.Sort();
        // Lag: the time shift of the true path that best matches the cursor path.
        var bestShift = 0;
        var bestError = double.MaxValue;
        for (var shift = 0; shift <= 48; shift++) // up to 200 ms
        {
            double sum = 0;
            for (var i = shift; i < cursors.Count; i++)
                sum += Math.Abs(cursors[i].X - truths[i - shift].X) + Math.Abs(cursors[i].Y - truths[i - shift].Y);
            sum /= cursors.Count - shift;
            if (sum < bestError) { bestError = sum; bestShift = shift; }
        }
        return (errors.Average(), errors[(int)(errors.Count * 0.95)], bestShift * 1000.0 / 240);
    }

    private static double StillSpread(PointerSettings s, int seed, double latency, double interval)
    {
        var rig = new Rig(s, SimulationPattern.HoldStill, seed, latency, interval);
        var xs = new List<double>();
        var ys = new List<double>();
        for (var t = 0.0; t < 6; t += 1.0 / 240)
        {
            var (_, c) = rig.Step();
            if (t < 2) continue;
            xs.Add(c.X);
            ys.Add(c.Y);
        }
        return Math.Max(xs.Max() - xs.Min(), ys.Max() - ys.Min());
    }

    /// <summary>Largest distance the cursor travels past a target it jumps to (Steps pattern), along x.</summary>
    private static double Overshoot(PointerSettings s, int seed, double latency, double interval)
    {
        var rig = new Rig(s, SimulationPattern.Steps, seed, latency, interval);
        rig.Sim.TremorDegrees = 0;
        // The hand stops exactly at the corners, so the true extremes are the targets: overshoot is how far the
        // cursor goes beyond them.
        double truthMinX = double.MaxValue, truthMaxX = double.MinValue, cursorMinX = double.MaxValue, cursorMaxX = double.MinValue;
        for (var t = 0.0; t < 12; t += 1.0 / 240)
        {
            var (truth, c) = rig.Step();
            if (t < 1) continue;
            truthMinX = Math.Min(truthMinX, truth.X); truthMaxX = Math.Max(truthMaxX, truth.X);
            cursorMinX = Math.Min(cursorMinX, c.X); cursorMaxX = Math.Max(cursorMaxX, c.X);
        }
        return Math.Max(0, Math.Max(cursorMaxX - truthMaxX, truthMinX - cursorMinX));
    }

    private sealed class FixedLayout(DisplayLayout layout) : IDisplayProvider
    {
        public DisplayLayout Current => layout;
        public event Action<DisplayLayout>? Changed { add { } remove { } }
    }
}
