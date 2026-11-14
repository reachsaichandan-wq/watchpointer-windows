using WatchPointer.Core.Pointer;
using WatchPointer.Core.Simulation;

namespace WatchPointer.Core.Tests;

/// <summary>End-to-end without hardware: virtual watch bytes → decoder → filter → projection → 240 Hz cursor loop.</summary>
public class SimulationIntegrationTests
{
    private static (EngineHarness H, List<(double T, double ExpectedX, int ActualX)> Trace) Run(SimulationPattern pattern, double seconds, double connectionInterval)
    {
        var h = new EngineHarness();
        var sim = new MotionSimulator { Pattern = pattern, ConnectionInterval = connectionInterval, TremorDegrees = 0.1 };
        var trace = new List<(double, double, int)>();
        var area = RectI.FromSize(0, 0, 1920, 1080);
        for (var t = 0.0; t < seconds; t += 1.0 / 240)
        {
            foreach (var packet in sim.Advance(h.Clock.Now)) h.Engine.HandleRaw(packet, h.Clock.Now);
            h.Engine.Tick();
            var expected = AbsoluteProjector.Project(sim.CurrentLeft, sim.CurrentUp, area, new PointerSettings());
            trace.Add((t, Math.Clamp(expected.X, 0, 1919), h.Sink.CursorPosition.X));
            h.Clock.Advance(1.0 / 240);
        }
        return (h, trace);
    }

    [Fact]
    public void Bursty_ble_delivery_still_gives_smooth_cursor_motion()
    {
        var (h, trace) = Run(SimulationPattern.Sweep, 6, connectionInterval: 0.030);
        var s = h.Engine.Snapshot();
        Assert.InRange(s.SampleRate, 90, 110);
        // Cursor velocity between ticks shouldn't spike: largest single-tick jump stays modest on a slow sweep.
        var jumps = trace.Skip(240).Zip(trace.Skip(241), (a, b) => Math.Abs(b.ActualX - a.ActualX)).ToList();
        Assert.True(jumps.Max() < 40, $"max jump {jumps.Max()} px per tick");
    }

    [Fact]
    public void Tracks_the_true_aim_with_bounded_lag()
    {
        var (_, trace) = Run(SimulationPattern.Circle, 8, connectionInterval: 0.015);
        var errors = trace.Skip(480).Select(p => Math.Abs(p.ExpectedX - p.ActualX)).OrderBy(e => e).ToList();
        var p95 = errors[(int)(errors.Count * 0.95)];
        // A 0.25 Hz circle of ±18° moves up to ~1100 px/s; 95 % of the time within 60 px (≈ 55 ms of lag) is fine.
        Assert.True(p95 < 60, $"p95 tracking error {p95:F1} px");
    }

    [Fact]
    public void Holding_still_keeps_the_cursor_within_a_few_pixels()
    {
        var (_, trace) = Run(SimulationPattern.HoldStill, 5, connectionInterval: 0.030);
        var settled = trace.Skip(480).Select(p => p.ActualX).ToList();
        Assert.True(settled.Max() - settled.Min() <= 6, $"spread {settled.Max() - settled.Min()} px");
    }

    [Fact]
    public void Simulator_emits_info_then_100_hz_timed_aims()
    {
        var sim = new MotionSimulator { ConnectionInterval = 0 };
        var packets = sim.Advance(1000).Concat(Enumerable.Range(1, 100).SelectMany(i => sim.Advance(1000 + i * 0.01))).ToList();
        Assert.Equal(6, packets[0][0]);
        Assert.InRange(packets.Count(p => p[0] == 0 && p.Length == 7), 95, 102);
    }
}
