using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WatchPointer.Core.Bluetooth;
using WatchPointer.Core.Diagnostics;
using WatchPointer.Core.Input;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Protocol;
using WatchPointer.Core.Session;
using WatchPointer.Core.Simulation;
using WatchPointer.Platform.Bluetooth;
using WatchPointer.Platform.Displays;
using WatchPointer.Platform.Input;
using WatchPointer.Platform.Timing;

DpiAwareness.EnablePerMonitorV2();
var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
var options = args.Skip(1).ToArray();
using var loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; })
    .SetMinimumLevel(Has("--verbose") ? LogLevel.Debug : LogLevel.Information));

return command switch
{
    "ble-check" => await BleCheck(),
    "listen" => await Listen(),
    "simulate" => await Simulate(),
    "bench" => Bench(),
    "track" => Track(),
    "displays" => Displays(),
    _ => Help(),
};

bool Has(string flag) => options.Contains(flag, StringComparer.OrdinalIgnoreCase);

string? Value(string name)
{
    var i = Array.FindIndex(options, o => string.Equals(o, name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < options.Length ? options[i + 1] : null;
}

double Seconds(double fallback) => double.TryParse(Value("--seconds"), out var s) ? s : fallback;

int Help()
{
    Console.WriteLine("""
        WatchPointer for Windows - command-line tool

          watchpointer-cli ble-check                 Report whether this PC's Bluetooth adapter can host WatchPointer
          watchpointer-cli listen [--move] [--seconds N] [--verbose]
                                                 Run the GATT server headless and print what arrives.
                                                 Without --move the cursor is not touched (safe for testing
                                                 with nRF Connect / LightBlue on a phone).
          watchpointer-cli simulate [--pattern circle|figureeight|sweep|holdstill|steps] [--seconds N] [--live]
                                                 Drive the real engine with a virtual watch. --live moves the cursor.
          watchpointer-cli bench [--seconds N]       Measure engine CPU cost per packet and per tick
          watchpointer-cli track                     Cursor-vs-hand accuracy for each responsiveness preset
          watchpointer-cli displays                  Print the monitor layout as WatchPointer sees it
        """);
    return 0;
}

async Task<int> BleCheck()
{
    await using var transport = new GattPeripheralTransport(StopwatchClock.Instance, loggerFactory.CreateLogger<GattPeripheralTransport>());
    var caps = await transport.GetCapabilitiesAsync();
    Console.WriteLine($"OS                          {caps.OsVersion}");
    Console.WriteLine($"Adapter present             {caps.AdapterPresent}");
    Console.WriteLine($"Adapter                     {caps.AdapterName} ({caps.AdapterAddress})");
    Console.WriteLine($"Bluetooth LE                {caps.LowEnergySupported}");
    Console.WriteLine($"Peripheral role (required)  {caps.PeripheralRoleSupported}");
    Console.WriteLine($"Central role                {caps.CentralRoleSupported}");
    Console.WriteLine($"Extended advertising        {caps.ExtendedAdvertisingSupported?.ToString() ?? "unknown"}");
    Console.WriteLine($"Max advertisement data      {caps.MaxAdvertisementDataLength?.ToString() ?? "unknown"}");
    Console.WriteLine($"Radio on                    {caps.RadioOn}");
    Console.WriteLine();
    if (!caps.CanHostWatchPointer || !caps.RadioOn)
    {
        Console.WriteLine($"NOT READY: {caps.Problem}");
        return 2;
    }

    Console.WriteLine("Capabilities look good. Trying to publish the GATT service and advertise...");
    var result = await transport.StartAsync();
    if (!result.Success)
    {
        Console.WriteLine($"FAILED: {result.Error}");
        return 3;
    }
    Console.WriteLine($"OK: advertising service {ProtocolConstants.ServiceUuid} (advertising={transport.IsAdvertising}).");
    Console.WriteLine("A watch (or a BLE scanner app on a phone) should now see this PC. Stopping.");
    await transport.StopAsync();
    return 0;
}

async Task<int> Listen()
{
    var clock = StopwatchClock.Instance;
    using var displays = new Win32DisplayProvider(loggerFactory.CreateLogger<Win32DisplayProvider>());
    IInputSink sink = Has("--move") ? new Win32InputSink(loggerFactory.CreateLogger<Win32InputSink>()) : new RecordingInputSink();
    var engine = new PointerEngine(sink, displays, clock, loggerFactory.CreateLogger<PointerEngine>());
    await using var actions = new ActionRunner(sink, loggerFactory.CreateLogger<ActionRunner>());
    engine.Actions = actions;
    engine.Notice += n => Console.WriteLine($"  [{n.Notice}] {n.Detail}");
    await using var transport = new GattPeripheralTransport(clock, loggerFactory.CreateLogger<GattPeripheralTransport>());
    transport.PacketReceived += (data, _) =>
    {
        if (Has("--verbose")) Console.WriteLine($"  rx {Convert.ToHexString(data.Span)}");
    };
    await using var supervisor = new ReceiverSupervisor(transport, engine, clock, loggerFactory.CreateLogger<ReceiverSupervisor>());
    supervisor.StateChanged += (s, d) => Console.WriteLine($"  transport: {s} {d}");
    using var loop = new CursorLoop(engine.Tick, 240, loggerFactory.CreateLogger<CursorLoop>());
    loop.Start();
    await supervisor.StartAsync();

    Console.WriteLine(Has("--move") ? "Listening; the watch WILL move the cursor. Ctrl+C to stop." : "Listening (cursor untouched; add --move to control it). Ctrl+C to stop.");
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    var seconds = Seconds(double.PositiveInfinity);
    var end = clock.Now + seconds;
    while (!cts.IsCancellationRequested && clock.Now < end)
    {
        try { await Task.Delay(1000, cts.Token); } catch (OperationCanceledException) { break; }
        var s = engine.Snapshot();
        Console.WriteLine($"{supervisor.State,-12} {s.Phase,-8} packets {s.Packets,6} invalid {s.InvalidPackets,3} " +
                          $"rate {s.SampleRate,5:F0}/s  aim {s.AimLeft,7:F2}° {s.AimUp,7:F2}°  cursor {s.Cursor.X},{s.Cursor.Y}" +
                          (s.RoundTrip.HasData ? $"  rtt p50 {s.RoundTrip.P50 * 1000:F1} ms" : "") +
                          (s.Watch is { } w ? $"  {w.Name} ({w.ModelName}) battery {w.Battery}%" : ""));
    }
    await supervisor.StopAsync();
    return 0;
}

async Task<int> Simulate()
{
    var clock = StopwatchClock.Instance;
    using var displays = new Win32DisplayProvider();
    IInputSink sink = Has("--live") ? new Win32InputSink() : new RecordingInputSink();
    var engine = new PointerEngine(sink, displays, clock);
    var pattern = Enum.TryParse<SimulationPattern>(Value("--pattern"), true, out var p) ? p : SimulationPattern.Circle;
    var sim = new MotionSimulator { Pattern = pattern };
    using var loop = new CursorLoop(engine.Tick);
    loop.Start();
    var seconds = Seconds(5);
    Console.WriteLine($"Simulating '{pattern}' for {seconds} s ({(Has("--live") ? "moving the real cursor" : "preview only")}).");
    var end = clock.Now + seconds;
    var nextReport = clock.Now + 1;
    while (clock.Now < end)
    {
        foreach (var packet in sim.Advance(clock.Now)) engine.HandleRaw(packet, clock.Now);
        await Task.Delay(2);
        if (clock.Now >= nextReport)
        {
            nextReport += 1;
            var s = engine.Snapshot();
            Console.WriteLine($"rate {s.SampleRate,5:F0}/s  ticks {s.TickRate,5:F0}/s  cursor {s.Cursor.X,5},{s.Cursor.Y,5}  " +
                              $"pipeline p50 {s.PipelineLatency.P50 * 1000:F2} ms p95 {s.PipelineLatency.P95 * 1000:F2} ms  " +
                              $"timer late p95 {loop.WakeLateness.P95 * 1000:F2} ms  hi-res timer {loop.UsingHighResolutionTimer}");
        }
    }
    return 0;
}

int Bench()
{
    // Virtual time: the engine and simulator are driven as fast as possible; wall time is measured per operation.
    var clock = new ManualClock();
    var displays = new StaticDisplays(DisplayLayout.Single(2560, 1440));
    var engine = new PointerEngine(new NullInputSink(), displays, clock);
    var sim = new MotionSimulator { Pattern = SimulationPattern.FigureEight, ConnectionInterval = 0.015 };
    var seconds = Seconds(60);
    var handle = new RollingWindow(100_000);
    var tick = new RollingWindow(100_000);
    long packets = 0, ticks = 0;
    var sw = new Stopwatch();
    var gcBefore = GC.CollectionCount(0);
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (var t = 0.0; t < seconds; t += 1.0 / 240)
    {
        clock.Now = 1000 + t;
        foreach (var packet in sim.Advance(clock.Now))
        {
            sw.Restart();
            engine.HandleRaw(packet, clock.Now);
            handle.Add(sw.Elapsed.TotalMicroseconds);
            packets++;
        }
        sw.Restart();
        engine.Tick();
        tick.Add(sw.Elapsed.TotalMicroseconds);
        ticks++;
    }
    var h = handle.Summarize();
    var k = tick.Summarize();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Console.WriteLine($"Simulated {seconds:F0} s: {packets} packets, {ticks} ticks");
    Console.WriteLine($"HandleRaw  mean {h.Mean:F2} µs  p50 {h.P50:F2} µs  p95 {h.P95:F2} µs  max {h.Max:F1} µs");
    Console.WriteLine($"Tick       mean {k.Mean:F2} µs  p50 {k.P50:F2} µs  p95 {k.P95:F2} µs  max {k.Max:F1} µs");
    Console.WriteLine($"CPU at 100 Hz packets + 240 Hz ticks ≈ {(h.Mean * 100 + k.Mean * 240) / 1e4:F3} % of one core");
    Console.WriteLine($"Allocated {allocated / 1024.0 / seconds:F1} KiB per simulated second, gen0 GCs {GC.CollectionCount(0) - gcBefore} (includes the simulator)");
    return 0;
}

int Track()
{
    // Cursor-vs-hand accuracy for each responsiveness preset, under a few Bluetooth conditions.
    foreach (var (name, latency, interval) in new[] { ("good link (12 ms, 15 ms interval)", 0.012, 0.015), ("typical (12 ms, 30 ms interval)", 0.012, 0.030), ("slow (25 ms, 45 ms interval)", 0.025, 0.045) })
    {
        Console.WriteLine(name);
        foreach (var preset in Enum.GetValues<ResponsivenessPreset>())
        {
            var settings = PointerPresets.Apply(new PointerSettings(), preset);
            Console.WriteLine($"  {preset,-9} {TrackingBenchmark.Run(settings, linkLatency: latency, connectionInterval: interval)}");
        }
    }
    return 0;
}

int Displays()
{
    using var displays = new Win32DisplayProvider();
    var layout = displays.Current;
    foreach (var d in layout.Displays)
        Console.WriteLine($"{d.Name,-20} {d.Bounds,-28} work {d.WorkArea,-28} scale {d.Scale * 100:F0}% {(d.IsPrimary ? "primary" : "")}");
    Console.WriteLine($"Virtual desktop: {layout.VirtualBounds}");
    return 0;
}

sealed class StaticDisplays(DisplayLayout layout) : IDisplayProvider
{
    public DisplayLayout Current => layout;
    public event Action<DisplayLayout>? Changed { add { } remove { } }
}
