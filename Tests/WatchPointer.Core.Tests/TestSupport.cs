using WatchPointer.Core.Diagnostics;
using WatchPointer.Core.Input;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Protocol;
using WatchPointer.Core.Session;

namespace WatchPointer.Core.Tests;

internal sealed class FixedDisplays(DisplayLayout layout) : IDisplayProvider
{
    public DisplayLayout Current { get; set; } = layout;
    public event Action<DisplayLayout>? Changed { add { } remove { } }
}

/// <summary>Drives the engine like the real receiver: 100 Hz timed aim packets and 240 Hz ticks on a manual clock.</summary>
internal sealed class EngineHarness
{
    public ManualClock Clock { get; } = new();
    public RecordingInputSink Sink { get; } = new();
    public FixedDisplays Displays { get; }
    public PointerEngine Engine { get; }
    public List<EngineNoticeArgs> Notices { get; } = [];
    public double WatchTime { get; private set; } = 10;

    public EngineHarness(DisplayLayout? layout = null, PointerSettings? settings = null)
    {
        Displays = new FixedDisplays(layout ?? DisplayLayout.Single(1920, 1080));
        Engine = new PointerEngine(Sink, Displays, Clock);
        if (settings is not null) Engine.Settings = settings;
        Engine.Notice += n => Notices.Add(n);
    }

    public ushort Ms => PacketCodec.ToWatchMs(WatchTime);

    public void Send(byte[] packet) => Engine.HandleRaw(packet, Clock.Now);

    /// <summary>Holds an aim for <paramref name="seconds"/>, sending samples at 100 Hz and ticking at 240 Hz.</summary>
    public void Aim(double left, double up, double seconds = 0.5, Func<double, (double, double)>? path = null)
    {
        const double sample = 0.01, tick = 1.0 / 240;
        var end = Clock.Now + seconds;
        var nextSample = Clock.Now;
        while (Clock.Now < end)
        {
            if (Clock.Now >= nextSample)
            {
                var (l, u) = path?.Invoke(Clock.Now) ?? (left, up);
                Send(PacketCodec.EncodeAim(l, u, Ms));
                nextSample += sample;
                WatchTime += sample;
            }
            Engine.Tick();
            Clock.Advance(tick);
        }
    }

    public void Idle(double seconds)
    {
        var end = Clock.Now + seconds;
        while (Clock.Now < end) { Engine.Tick(); Clock.Advance(1.0 / 240); WatchTime += 1.0 / 240; }
    }
}
