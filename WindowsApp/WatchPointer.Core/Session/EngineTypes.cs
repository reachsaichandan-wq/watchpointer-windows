using WatchPointer.Core.Diagnostics;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Profiles;
using WatchPointer.Core.Protocol;

namespace WatchPointer.Core.Session;

public enum PointerPhase
{
    /// <summary>No watch connected.</summary>
    Waiting,
    /// <summary>Samples are arriving and moving the cursor.</summary>
    Live,
    /// <summary>The watch's Pause button is on.</summary>
    Paused,
    /// <summary>Connected but no samples for a while (watch asleep, app in background, out of range).</summary>
    Idle,
}

public enum EngineNotice
{
    Connected,
    Lost,
    Disconnected,
    Centered,
    Paused,
    Resumed,
    PcPaused,
    PcResumed,
    Grabbed,
    Dropped,
    WatchInfo,
    UntrustedWatch,
    Gesture,
    SpeedModeChanged,
    DragLockChanged,
}

public readonly record struct EngineNoticeArgs(EngineNotice Notice, string? Detail = null);

/// <summary>Everything the dashboard and diagnostics show, captured atomically.</summary>
public sealed record EngineSnapshot
{
    public PointerPhase Phase { get; init; }
    public bool PcPaused { get; init; }
    public bool Dragging { get; init; }
    public bool DragLocked { get; init; }
    public SpeedMode SpeedMode { get; init; }
    public bool AutoPrecisionActive { get; init; }
    public PauseReason PauseReason { get; init; }
    public WatchInfo? Watch { get; init; }
    public bool WatchUntrusted { get; init; }
    public PointerMode Mode { get; init; }
    public string ProfileName { get; init; } = "";
    public double SampleRate { get; init; }
    public double TickRate { get; init; }
    public double AimLeft { get; init; }
    public double AimUp { get; init; }
    public PointI Cursor { get; init; }
    public long Packets { get; init; }
    public long InvalidPackets { get; init; }
    public IReadOnlyDictionary<DecodeError, long> InvalidByReason { get; init; } = new Dictionary<DecodeError, long>();
    public long Clicks { get; init; }
    /// <summary>Aim samples dropped because a newer one had already arrived.</summary>
    public long ReorderedSamples { get; init; }
    public double SecondsSinceLastPacket { get; init; }
    /// <summary>Bluetooth delay of each sample beyond the quickest one observed (queueing + jitter), seconds.</summary>
    public RollingWindow.Summary LinkExcessDelay { get; init; }
    /// <summary>Gap between consecutive sample arrivals, seconds (shows BLE burstiness).</summary>
    public RollingWindow.Summary ArrivalInterval { get; init; }
    /// <summary>Arrival of a sample → first cursor update that includes it, seconds (PC-side processing latency).</summary>
    public RollingWindow.Summary PipelineLatency { get; init; }
    /// <summary>Measured BLE round trip (v2 watches only), seconds.</summary>
    public RollingWindow.Summary RoundTrip { get; init; }
    /// <summary>Time spent in one engine tick, seconds (CPU cost).</summary>
    public RollingWindow.Summary TickCost { get; init; }

    /// <summary>Estimated motion-to-cursor latency: RTT/2 (one-way BLE) + PC pipeline. Null without RTT data.</summary>
    public double? EstimatedLatency =>
        RoundTrip.HasData && PipelineLatency.HasData ? RoundTrip.P50 / 2 + PipelineLatency.P50 : null;
}
