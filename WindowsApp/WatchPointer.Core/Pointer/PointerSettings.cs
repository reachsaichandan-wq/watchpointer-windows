namespace WatchPointer.Core.Pointer;

public enum PointerMode
{
    /// <summary>Laser pointer: watch orientation decides the cursor position.</summary>
    Absolute,
    /// <summary>Air mouse: wrist rotation speed moves the cursor incrementally.</summary>
    Relative,
}

/// <summary>All pointer tuning. Geometry defaults match the original WatchPointer; responsiveness defaults are the
/// "Balanced" preset (the original tuning is the "Smooth" preset, see <see cref="PointerPresets"/>).</summary>
public sealed record PointerSettings
{
    public PointerMode Mode { get; init; } = PointerMode.Absolute;

    // ---- Absolute ----
    /// <summary>Horizontal speed. Turn needed to reach the left/right edge = <see cref="BaseReachDegrees"/> / speed.</summary>
    public double HorizontalSpeed { get; init; } = 1.0;
    /// <summary>Vertical speed. 1.0 = same pixels-per-degree scale as horizontal (original behaviour).</summary>
    public double VerticalSpeed { get; init; } = 1.0;
    /// <summary>Aim angles are clamped to this before projecting (tan() explodes near 90°).</summary>
    public double MaxAngleDegrees { get; init; } = 80;
    public PointingArea PointingArea { get; init; } = PointingArea.PrimaryDisplay;
    public string? PointingDisplayId { get; init; }
    /// <summary>When pointing past the outer edge, shift the centre so coming back moves the cursor immediately
    /// (re-anchors slow heading drift). Off = original behaviour (cursor pins, lines up again on return).</summary>
    public bool ReanchorAtEdges { get; init; }

    // ---- Relative ----
    /// <summary>Pixels per degree of wrist rotation at slow speeds.</summary>
    public double RelativeSpeed { get; init; } = 25;
    /// <summary>0 = linear. Higher = fast flicks travel further (pointer acceleration).</summary>
    public double RelativeAcceleration { get; init; } = 0.6;
    /// <summary>Angular speeds below this (degrees/second) are ignored: suppresses tremor and slow drift.</summary>
    public double RelativeDeadZoneDegPerSec { get; init; } = 2.0;

    // ---- Shared ----
    public bool InvertHorizontal { get; init; }
    public bool InvertVertical { get; init; }
    /// <summary>1€ min cutoff, Hz ("steady").</summary>
    public double SmoothingMinCutoff { get; init; } = 1.0;
    /// <summary>1€ beta ("quick").</summary>
    public double SmoothingBeta { get; init; } = 0.02;
    /// <summary>Catch-up time constant of the 240 Hz glide between samples, seconds.</summary>
    public double FollowTimeConstant { get; init; } = 0.003;
    /// <summary>Cursor loop rate. 240 Hz by default (faster than any display, hides the gaps between BLE samples).</summary>
    public int CursorRateHz { get; init; } = 240;
    /// <summary>Motion prediction: how far ahead (ms) to extrapolate the pointer along its current velocity, to hide
    /// Bluetooth delay. Also fills the gaps between bursty BLE deliveries. Only active on deliberate movement (fades
    /// out below ~150 px/s), so holding still stays steady. 0 = off (original behaviour).</summary>
    public double PredictionMs { get; init; } = 20;
    /// <summary>When the watch reports round trips (protocol v2), size the prediction from the measured one-way
    /// Bluetooth delay (RTT/2 + filter lag) instead of the fixed <see cref="PredictionMs"/>.</summary>
    public bool AdaptivePrediction { get; init; } = true;

    // ---- Scrolling ----
    /// <summary>Multiplier on the watch's Crown scroll amount.</summary>
    public double ScrollSpeed { get; init; } = 1.0;
    public bool InvertScroll { get; init; }
    /// <summary>Send partial wheel deltas (smooth scrolling in modern apps) instead of whole 120-unit notches.</summary>
    public bool SmoothScroll { get; init; } = true;

    // ---- Clicks ----
    /// <summary>When the watch sends no touch-down time (older builds), rewind this long before the click arrived.</summary>
    public double FallbackRewindSeconds { get; init; } = 0.1;

    public const double BaseReachDegrees = 25.0;

    public PointerSettings Sanitized() => this with
    {
        HorizontalSpeed = Math.Clamp(HorizontalSpeed, 0.1, 10),
        VerticalSpeed = Math.Clamp(VerticalSpeed, 0.1, 10),
        MaxAngleDegrees = Math.Clamp(MaxAngleDegrees, 10, 85),
        RelativeSpeed = Math.Clamp(RelativeSpeed, 1, 500),
        RelativeAcceleration = Math.Clamp(RelativeAcceleration, 0, 5),
        RelativeDeadZoneDegPerSec = Math.Clamp(RelativeDeadZoneDegPerSec, 0, 60),
        SmoothingMinCutoff = Math.Clamp(SmoothingMinCutoff, 0.05, 30),
        SmoothingBeta = Math.Clamp(SmoothingBeta, 0, 1),
        FollowTimeConstant = Math.Clamp(FollowTimeConstant, 0.0005, 0.1),
        CursorRateHz = Math.Clamp(CursorRateHz, 60, 1000),
        PredictionMs = Math.Clamp(PredictionMs, 0, 80),
        ScrollSpeed = Math.Clamp(ScrollSpeed, 0.05, 20),
        FallbackRewindSeconds = Math.Clamp(FallbackRewindSeconds, 0, 0.5),
    };
}
