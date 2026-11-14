using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WatchPointer.Core.Diagnostics;
using static WatchPointer.Platform.Native.NativeMethods;

namespace WatchPointer.Platform.Timing;

/// <summary>
/// Calls a tick function at a fixed rate (default 240 Hz) on its own highest-priority thread, so UI work can never
/// delay the cursor. Uses a high-resolution waitable timer (Windows 10 1803+) with absolute deadlines (no drift);
/// falls back to timeBeginPeriod(1) + sleeps on older systems. When the tick reports nothing to do (no watch steering),
/// the loop drops to 20 Hz to save power.
/// </summary>
public sealed class CursorLoop : IDisposable
{
    private readonly Func<bool> _tick;
    private readonly ILogger _log;
    private readonly Thread _thread;
    private readonly RollingWindow _jitter = new(512);
    private readonly object _statsGate = new();
    private volatile bool _stop;
    private volatile int _rateHz;

    public CursorLoop(Func<bool> tick, int rateHz = 240, ILogger<CursorLoop>? log = null)
    {
        _tick = tick;
        _rateHz = rateHz;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _thread = new Thread(Run) { IsBackground = true, Name = "WatchPointer cursor loop", Priority = ThreadPriority.Highest };
    }

    public int RateHz { get => _rateHz; set => _rateHz = Math.Clamp(value, 20, 1000); }
    public const int IdleRateHz = 20;
    public bool UsingHighResolutionTimer { get; private set; }
    public bool Active { get; private set; }

    /// <summary>Lateness of each wake-up versus its deadline, seconds.</summary>
    public RollingWindow.Summary WakeLateness { get { lock (_statsGate) return _jitter.Summarize(); } }

    public void Start() => _thread.Start();

    private void Run()
    {
        var timer = CreateWaitableTimerEx(0, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        UsingHighResolutionTimer = timer != 0;
        if (!UsingHighResolutionTimer)
        {
            _log.LogInformation("High-resolution waitable timer unavailable; using timeBeginPeriod(1)");
            timeBeginPeriod(1);
        }
        try
        {
            var frequency = (double)Stopwatch.Frequency;
            var next = Stopwatch.GetTimestamp();
            while (!_stop)
            {
                bool active;
                try { active = _tick(); }
                catch (Exception ex) { _log.LogError(ex, "Cursor tick failed"); active = false; }
                Active = active;

                var period = (long)(frequency / (active ? _rateHz : IdleRateHz));
                next += period;
                var now = Stopwatch.GetTimestamp();
                if (now - next > period * 4) next = now + period; // fell far behind (system sleep): resync
                var wait = next - now;
                if (wait > 0)
                {
                    if (UsingHighResolutionTimer)
                    {
                        var due = -(long)(wait * 10_000_000.0 / frequency); // negative = relative, 100 ns units
                        SetWaitableTimer(timer, in due, 0, 0, 0, false);
                        WaitForSingleObject(timer, INFINITE);
                    }
                    else
                    {
                        Thread.Sleep(Math.Max(1, (int)(wait * 1000 / frequency)));
                    }
                }
                if (active)
                {
                    var late = (Stopwatch.GetTimestamp() - next) / frequency;
                    lock (_statsGate) _jitter.Add(late);
                }
            }
        }
        finally
        {
            if (timer != 0) CloseHandle(timer); else timeEndPeriod(1);
        }
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive) _thread.Join(500);
    }
}
