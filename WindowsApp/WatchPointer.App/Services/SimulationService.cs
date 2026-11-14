using WatchPointer.Core.Simulation;

namespace WatchPointer.App.Services;

/// <summary>
/// Runs the virtual watch against the real engine. In preview mode the engine's output goes to a recording sink
/// (the Diagnostics page draws it); in live mode it moves the real cursor. Real watch input is suspended meanwhile.
/// </summary>
public sealed class SimulationService(ReceiverHost host)
{
    private CancellationTokenSource? _cts;
    private Thread? _thread;

    public MotionSimulator Simulator { get; } = new();
    public bool Running => _thread is { IsAlive: true };
    public bool Live { get; private set; }

    public void Start(SimulationPattern pattern, bool live)
    {
        Stop();
        Simulator.Pattern = pattern;
        Simulator.Reset();
        Live = live;
        host.Supervisor.InputEnabled = false;
        host.Engine.WatchDisconnected();
        host.Input.Target = live ? host.RealInput : host.Preview;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _thread = new Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                var now = host.Clock.Now;
                foreach (var packet in Simulator.Advance(now)) host.Engine.HandleRaw(packet, now);
                Thread.Sleep(1);
            }
        }) { IsBackground = true, Name = "WatchPointer simulator", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _thread?.Join(500);
        _cts.Dispose();
        _cts = null;
        _thread = null;
        host.Engine.WatchDisconnected();
        host.Input.Target = host.RealInput;
        host.Supervisor.InputEnabled = true;
    }
}
