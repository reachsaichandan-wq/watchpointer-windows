using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WatchPointer.Core.Diagnostics;
using WatchPointer.Core.Input;
using WatchPointer.Core.Motion;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Profiles;
using WatchPointer.Core.Protocol;

namespace WatchPointer.Core.Session;

/// <summary>
/// Turns decoded watch packets into cursor movement, clicks, drags, scrolling and shortcuts.
/// <para>Threading: <see cref="HandleRaw"/> is called from the Bluetooth thread and <see cref="Tick"/> from the
/// high-priority cursor loop; both take one short lock. Notices are raised after the lock is released.</para>
/// <para>Behaviour follows the original macOS receiver: 1€ filtering with the watch's own timestamps, a 240 Hz glide
/// toward the newest sample, clicks placed where the pointer was at touch-down (so the tap's jolt doesn't move them),
/// drop on silence, idle after 1.5 s.</para>
/// </summary>
public sealed class PointerEngine
{
    /// <summary>A gap longer than this starts fresh: no gliding from the old spot (original: 0.3 s).</summary>
    public const double FreshGapSeconds = 0.3;
    /// <summary>Silence after which a live watch is reported lost (original: 1.5 s).</summary>
    public const double LostAfterSeconds = 1.5;
    /// <summary>Watch dt values outside (0, 0.2) s are treated as discontinuities.</summary>
    public const double MaxSampleGapSeconds = 0.2;
    /// <summary>Rewind for a hold when the watch sends no time: its hold-to-drag delay plus a press (original: 0.45 s).</summary>
    public const double GrabFallbackRewindSeconds = 0.45;
    /// <summary>Crown "pixels" per Windows wheel notch (120 units). A notch scrolls ~3 lines ≈ 50 px.</summary>
    public const double CrownPixelsPerNotch = 50;
    /// <summary>Relative mode filters angles in this many "reference pixels" per degree so the 1€ parameters mean
    /// roughly the same thing in both modes (≈ px/° of a 1920 px wide display at speed 1).</summary>
    public const double RelativeFilterScale = 36;

    private readonly object _gate = new();
    private readonly IInputSink _input;
    private readonly IDisplayProvider _displays;
    private readonly IClock _clock;
    private readonly ILogger _log;
    private readonly List<EngineNoticeArgs> _pendingNotices = [];

    private PointerSettings _settings = new();
    private ControlProfile _profile = DefaultProfiles.CreateDesktop();
    private readonly OneEuroFilter _filterX = new(), _filterY = new();
    private readonly RelativeMover _relative = new();
    private readonly WatchClock _watchClock = new();

    // Pointer state (under _gate)
    private PointerPhase _phase = PointerPhase.Waiting;
    private bool _pcPaused;
    private bool _watchPaused;
    private bool _dragging;
    private bool _untrusted;
    private WatchInfo? _watch;
    private PointD? _target;
    private PointD _shown;
    private PointI? _lastPosted;
    private double _lastSample = double.NegativeInfinity;
    private double _lastTick;
    private double _aimLeft, _aimUp;          // latest raw aim from the watch
    private double _offsetLeft, _offsetUp;    // PC-side recenter / edge re-anchoring
    private double? _pendingArrival;          // newest sample not yet reflected on screen
    private double _scrollAccumulator, _horizontalAccumulator;
    private long _packets, _invalid, _clicks, _reordered;
    private readonly Dictionary<DecodeError, long> _invalidByReason = [];
    private readonly RateCounter _sampleRate = new(), _tickRate = new();
    private readonly RollingWindow _linkExcess = new(), _arrivalInterval = new(), _pipeline = new(), _tickCost = new(256);
    private readonly (double Time, PointD Point)[] _history = new (double, PointD)[1024];
    private int _historyStart, _historyCount;

    public PointerEngine(IInputSink input, IDisplayProvider displays, IClock clock, ILogger<PointerEngine>? log = null)
    {
        _input = input;
        _displays = displays;
        _clock = clock;
        _log = (ILogger?)log ?? NullLogger.Instance;
        ApplyFilterSettings();
    }

    /// <summary>Executes shortcut and app-level actions. Optional (tests may leave it null).</summary>
    public ActionRunner? Actions { get; set; }

    /// <summary>Decides whether a watch (from its info packet) may control this PC. Null = accept all.</summary>
    public Func<WatchInfo, bool>? AcceptWatch { get; set; }

    public LatencyProbe Probe { get; } = new();

    public event Action<EngineNoticeArgs>? Notice;

    public PointerSettings Settings
    {
        get { lock (_gate) return _settings; }
        set
        {
            lock (_gate)
            {
                var modeChanged = value.Mode != _settings.Mode;
                _settings = value.Sanitized();
                ApplyFilterSettings();
                if (modeChanged) ResetMotion();
            }
        }
    }

    public ControlProfile Profile
    {
        get { lock (_gate) return _profile; }
        set { lock (_gate) { _profile = value; _scrollAccumulator = 0; } }
    }

    public bool PcPaused
    {
        get { lock (_gate) return _pcPaused; }
        set
        {
            lock (_gate)
            {
                if (_pcPaused == value) return;
                _pcPaused = value;
                if (value) Drop(null);
                else ResetMotion();
                Raise(value ? EngineNotice.PcPaused : EngineNotice.PcResumed);
            }
            FlushNotices();
        }
    }

    // ------------------------------------------------------------------ packets

    /// <summary>Decodes and handles one write from the watch. Returns false (and counts it) when invalid.</summary>
    public bool HandleRaw(ReadOnlySpan<byte> data, double arrival)
    {
        if (!PacketCodec.TryDecode(data, out var packet, out var error))
        {
            lock (_gate)
            {
                _packets++;
                _invalid++;
                _invalidByReason[error] = _invalidByReason.GetValueOrDefault(error) + 1;
            }
            _log.LogDebug("Invalid packet ({Error}): {Hex}", error, Convert.ToHexString(data));
            return false;
        }
        Handle(packet!, arrival);
        return true;
    }

    public void Handle(WatchPacket packet, double arrival)
    {
        lock (_gate)
        {
            _packets++;
            if (_untrusted && packet is not InfoPacket) return;
            switch (packet)
            {
                case AimPacket aim: OnAim(aim, arrival); break;
                case ClickPacket click: OnInput(click.Button == MouseButton.Right ? WatchInput.RightButton : WatchInput.PadTap, When(click.WatchMs), arrival); break;
                case DragPacket drag: OnDrag(drag, arrival); break;
                case ScrollPacket scroll: OnScroll(scroll); break;
                case CenterPacket: OnWatchCenter(); break;
                case PausePacket pause: OnWatchPause(pause.Paused); break;
                case InfoPacket info: OnInfo(info.Info); break;
                case GesturePacket gesture:
                    if (DefaultProfiles.FromGesture(gesture.Gesture) is { } input)
                    {
                        Raise(EngineNotice.Gesture, gesture.Gesture.ToString());
                        OnInput(input, When(gesture.WatchMs), arrival);
                    }
                    break;
                case PongPacket pong: Probe.OnPong(pong.Sequence, arrival); break;
            }
        }
        FlushNotices();
    }

    /// <summary>The watch unsubscribed / the link closed.</summary>
    public void WatchDisconnected()
    {
        lock (_gate)
        {
            Drop(null);
            var wasConnected = _phase != PointerPhase.Waiting || _watch is not null;
            _watch = null;
            _untrusted = false;
            _watchPaused = false;
            _phase = PointerPhase.Waiting;
            _target = null;
            _watchClock.Reset();
            ResetMotion();
            Probe.Reset();
            if (wasConnected) Raise(EngineNotice.Disconnected);
        }
        FlushNotices();
    }

    private void OnAim(AimPacket aim, double now)
    {
        var fresh = now - _lastSample > FreshGapSeconds;
        if (!fresh && aim.WatchMs is { } stamp && _watchClock.IsStale(stamp))
        {
            _reordered++; // a newer sample was already applied; this one would only drag the pointer back
            return;
        }
        if (!fresh) _arrivalInterval.Add(now - _lastSample);
        if (_phase is PointerPhase.Waiting or PointerPhase.Idle && !_watchPaused)
        {
            _phase = PointerPhase.Live;
            Raise(EngineNotice.Connected);
        }

        // Time between measurements on the watch. A sample the link dropped just makes this gap longer.
        var dt = now - _lastSample;
        if (aim.WatchMs is { } ms)
        {
            if (_watchClock.Observe(ms, now, fresh) is { } watchDt) dt = watchDt;
            _linkExcess.Add(_watchClock.ExcessDelay(now));
        }
        _lastSample = now;
        _sampleRate.Hit(now);
        _aimLeft = aim.LeftDegrees;
        _aimUp = aim.UpDegrees;

        if (_pcPaused || !_profile.MovePointer) return;
        var validDt = dt > 0 && dt < MaxSampleGapSeconds ? dt : 0;
        var layout = _displays.Current;
        var left = _aimLeft - _offsetLeft;
        var up = _aimUp - _offsetUp;

        if (_settings.Mode == PointerMode.Absolute)
        {
            var area = layout.PointingArea(_settings.PointingArea, _settings.PointingDisplayId);
            var p = AbsoluteProjector.Project(left, up, area, _settings);
            if (_settings.ReanchorAtEdges)
            {
                var clamped = layout.Clamp(p);
                if ((clamped - p).ManhattanLength > 0.5)
                {
                    // Slide the centre so the edge is where we point now: coming back moves the cursor at once.
                    var (l, u) = AbsoluteProjector.Unproject(clamped, area, _settings);
                    _offsetLeft = _aimLeft - l;
                    _offsetUp = _aimUp - u;
                    p = clamped;
                }
            }
            if (fresh || _target is null)
            {
                _filterX.Reset();
                _filterY.Reset();
                _shown = p;
            }
            _target = new PointD(_filterX.Filter(p.X, validDt), _filterY.Filter(p.Y, validDt));
            _rawPoint = p;
        }
        else
        {
            var cursor = _input.CursorPosition;
            var resync = fresh || _target is null
                         || (_lastPosted is { } posted && Math.Abs(posted.X - cursor.X) + Math.Abs(posted.Y - cursor.Y) > 2);
            if (resync)
            {
                // Start (or resume after the physical mouse moved) from where the cursor actually is.
                _filterX.Reset();
                _filterY.Reset();
                _relative.Reset();
                _target = _shown = new PointD(cursor.X, cursor.Y);
                _lastPosted = cursor;
            }
            var fl = _filterX.Filter(left * RelativeFilterScale, validDt) / RelativeFilterScale;
            var fu = _filterY.Filter(up * RelativeFilterScale, validDt) / RelativeFilterScale;
            var (dx, dy) = _relative.Update(fl, fu, validDt, _settings);
            _target = layout.Clamp(_target!.Value + new PointD(dx, dy));
            _rawPoint = _target!.Value;
        }
        UpdateVelocity(_target!.Value, _rawPoint, validDt, fresh);
        _pendingArrival ??= now;
    }

    // ------------------------------------------------------------------ prediction

    /// <summary>Smoothing time constant of the velocity estimate used for prediction.</summary>
    public const double VelocityTimeConstant = 0.02;
    /// <summary>Prediction fades in between these speeds (px/s), so a still or slowly moving hand isn't amplified.</summary>
    public const double PredictionFadeStart = 150, PredictionFadeFull = 500;
    /// <summary>Between BLE bursts, keep extrapolating for at most this long after the newest sample.</summary>
    public const double MaxGapExtrapolation = 0.04;
    /// <summary>Hard cap on how far prediction may lead the measured position.</summary>
    public const double MaxPredictionPx = 120;

    /// <summary>Time constant of the unfiltered-velocity estimate used to notice deceleration quickly.</summary>
    public const double RawVelocityTimeConstant = 0.012;
    /// <summary>Added to RTT/2 in adaptive mode: the residual lag of the 1€ filter and the glide.</summary>
    public const double AdaptiveFilterLagMs = 8;

    private PointD _velocity, _rawVelocity, _rawPoint;
    private PointD? _velocityReference, _rawReference;

    private void UpdateVelocity(PointD target, PointD raw, double dt, bool fresh)
    {
        if (fresh || _velocityReference is null || _rawReference is null || !(dt > 0))
        {
            if (fresh || _velocityReference is null) { _velocity = _rawVelocity = _rawAcceleration = default; }
            _velocityReference = target;
            _rawReference = raw;
            return;
        }
        _velocity += ((target - _velocityReference.Value) * (1 / dt) - _velocity) * (1 - Math.Exp(-dt / VelocityTimeConstant));
        var previousRaw = _rawVelocity;
        _rawVelocity += ((raw - _rawReference.Value) * (1 / dt) - _rawVelocity) * (1 - Math.Exp(-dt / RawVelocityTimeConstant));
        _rawAcceleration += ((_rawVelocity - previousRaw) * (1 / dt) - _rawAcceleration) * (1 - Math.Exp(-dt / RawVelocityTimeConstant));
        _velocityReference = target;
        _rawReference = raw;
    }

    private PointD _rawAcceleration;

    /// <summary>Prediction lead in seconds: measured one-way delay when available (adaptive), else the setting.</summary>
    private double PredictionSeconds()
    {
        if (_settings.PredictionMs <= 0) return 0;
        if (_settings.AdaptivePrediction && Probe.RoundTrip is { Count: >= 3 } rtt)
            return Math.Clamp(rtt.P50 / 2 + AdaptiveFilterLagMs / 1000, 0, 0.08);
        return _settings.PredictionMs / 1000;
    }

    /// <summary>Where the cursor should be heading now: the filtered target, pushed ahead along the hand's velocity by
    /// the prediction time plus the time since the newest sample (so bursts of BLE samples don't show as steps).
    /// Prediction fades in with speed (a still hand isn't amplified) and fades out as soon as the unfiltered motion
    /// shows the hand slowing down (so stopping doesn't overshoot).</summary>
    private PointD Predict(PointD target, double now)
    {
        var lead = PredictionSeconds();
        if (lead <= 0) return target;
        var speed = Math.Sqrt(_velocity.X * _velocity.X + _velocity.Y * _velocity.Y);
        var gain = Math.Clamp((speed - PredictionFadeStart) / (PredictionFadeFull - PredictionFadeStart), 0, 1);
        if (gain <= 0) return target;
        // Component of the current (raw) velocity along the smoothed direction of travel: < speed when decelerating.
        var along = (_rawVelocity.X * _velocity.X + _rawVelocity.Y * _velocity.Y) / speed;
        gain *= Math.Clamp(along / speed, 0, 1);
        if (gain <= 0) return target;
        var ahead = lead + Math.Clamp(now - _lastSample, 0, MaxGapExtrapolation);
        var travel = speed * ahead * gain;
        // Decelerating? Never lead further than the hand needs to stop at its current deceleration (v²/2a).
        var accelAlong = (_rawAcceleration.X * _velocity.X + _rawAcceleration.Y * _velocity.Y) / speed;
        if (accelAlong < 0 && along > 0) travel = Math.Min(travel, along * along / (2 * -accelAlong));
        travel = Math.Min(travel, MaxPredictionPx);
        return target + _velocity * (travel / speed);
    }

    // ------------------------------------------------------------------ cursor loop

    /// <summary>Called by the cursor loop (240 Hz). Returns true while the watch is actively steering, so the loop
    /// can slow down when nothing is happening.</summary>
    public bool Tick()
    {
        bool active;
        lock (_gate)
        {
            var start = _clock.Now;
            active = TickLocked(start);
            if (active) _tickCost.Add(_clock.Now - start);
        }
        FlushNotices();
        return active;
    }

    private bool TickLocked(double now)
    {
        var frame = _lastTick > 0 ? Math.Clamp(now - _lastTick, 0, 0.05) : 1.0 / _settings.CursorRateHz;
        _lastTick = now;
        _tickRate.Hit(now);
        _sampleRate.Roll(now);
        if (_target is not { } target) return false;

        if (now - _lastSample >= FreshGapSeconds)
        {
            // Watch went quiet: leave the mouse alone, and never leave the button held down mid-drag.
            Drop(null);
            if (_phase == PointerPhase.Live && now - _lastSample > LostAfterSeconds)
            {
                _phase = PointerPhase.Idle;
                _log.LogInformation("Watch went quiet (no samples for {Seconds:F1} s)", now - _lastSample);
                Raise(EngineNotice.Lost);
            }
            return false;
        }
        if (_pcPaused || !_profile.MovePointer) return true;

        var follow = 1 - Math.Exp(-frame / _settings.FollowTimeConstant);
        var next = _shown + (Predict(target, now) - _shown) * follow;
        var moved = (next - _shown).ManhattanLength > 0.05;
        if (moved) _shown = next;
        var p = _displays.Current.Clamp(_shown);
        var pixel = new PointI((int)Math.Round(p.X), (int)Math.Round(p.Y));
        if (_lastPosted == pixel)
        {
            // Nothing new to show. (Unlike the original, a fresh jump with no further motion is still posted,
            // because the comparison is against what was last sent rather than against the previous frame.)
            if (_pendingArrival is { } waiting && (target - _shown).ManhattanLength <= 0.5)
            {
                _pipeline.Add(now - waiting); // already on screen
                _pendingArrival = null;
            }
            if (!moved) return true;
        }
        else
        {
            _input.MoveTo(pixel);
            _lastPosted = pixel;
        }
        AddHistory(now, p);
        if (_pendingArrival is { } arrival)
        {
            _pipeline.Add(now - arrival);
            _pendingArrival = null;
        }
        return true;
    }

    // ------------------------------------------------------------------ clicks, drag, gestures

    /// <summary>The PC time a click/drag packet refers to, from the watch time it carries (null for old watch builds).</summary>
    private double? When(ushort? watchMs) => watchMs is { } ms ? _watchClock.ToPcTime(ms) : null;

    private void OnInput(WatchInput input, double? time, double now)
    {
        var action = _profile.Resolve(input);
        Execute(action, time, now, input);
    }

    private void Execute(ActionSpec action, double? time, double now, WatchInput source)
    {
        switch (action.Type)
        {
            case ActionType.None: return;
            case ActionType.LeftClick or ActionType.Drag: Click(MouseButton.Left, time, now, 1); return;
            case ActionType.RightClick: Click(MouseButton.Right, time, now, 1); return;
            case ActionType.MiddleClick: Click(MouseButton.Middle, time, now, 1); return;
            case ActionType.DoubleClick: Click(MouseButton.Left, time, now, 2); return;
            case ActionType.Recenter: RecenterLocked(); return;
            case ActionType.TogglePause:
                _pcPaused = !_pcPaused;
                if (_pcPaused) Drop(null); else ResetMotion();
                Raise(_pcPaused ? EngineNotice.PcPaused : EngineNotice.PcResumed);
                return;
            case ActionType.Keys:
                if (Actions?.RunKeys(action.Keys) != true) _log.LogDebug("No runnable shortcut for {Input}", source);
                return;
            case ActionType.ToggleLaser: Actions?.RequestApp(AppAction.ToggleLaser); return;
            case ActionType.NextProfile: Actions?.RequestApp(AppAction.NextProfile); return;
            case ActionType.SwitchProfile: Actions?.RequestApp(AppAction.SwitchProfile, action.Profile); return;
        }
    }

    private void OnDrag(DragPacket drag, double now)
    {
        var action = _profile.Resolve(WatchInput.PadHold);
        var time = When(drag.WatchMs);
        if (action.Type == ActionType.Drag)
        {
            if (drag.Grab) Grab(time, now); else Drop(time);
        }
        else if (drag.Grab)
        {
            Execute(action, time, now, WatchInput.PadHold); // other actions fire once, on press
        }
    }

    /// <summary>Where to press: while the watch is steering, where the pointer was at <paramref name="time"/>
    /// (touch-down, before the tap jolted the watch). Otherwise the current cursor.</summary>
    private PointI PressPoint(double? time, double now, double fallbackRewind)
    {
        var current = _input.CursorPosition;
        var moment = time ?? now - fallbackRewind;
        if (_phase != PointerPhase.Live || _pcPaused || now - _lastSample >= FreshGapSeconds || _historyCount == 0) return current;
        var before = HistoryAt(moment);
        _shown = before;
        return new PointI((int)Math.Round(before.X), (int)Math.Round(before.Y));
    }

    private void Click(MouseButton button, double? time, double now, int count)
    {
        var p = PressPoint(time, now, _settings.FallbackRewindSeconds);
        MoveIfNeeded(p);
        for (var i = 0; i < count; i++)
        {
            _input.Button(button, true);
            _input.Button(button, false);
        }
        _clicks++;
    }

    private void Grab(double? time, double now)
    {
        if (_dragging) return;
        var p = PressPoint(time, now, GrabFallbackRewindSeconds);
        MoveIfNeeded(p);
        _input.Button(MouseButton.Left, true);
        _dragging = true;
        Raise(EngineNotice.Grabbed);
    }

    private void Drop(double? time)
    {
        if (!_dragging) return;
        var p = PressPoint(time, _clock.Now, 0);
        MoveIfNeeded(p);
        _input.Button(MouseButton.Left, false);
        _dragging = false;
        Raise(EngineNotice.Dropped);
    }

    private void MoveIfNeeded(PointI p)
    {
        if (_lastPosted == p && _input.CursorPosition == p) return;
        _input.MoveTo(p);
        _lastPosted = p;
    }

    // ------------------------------------------------------------------ scroll

    private void OnScroll(ScrollPacket scroll)
    {
        var sign = _settings.InvertScroll ? -1 : 1;
        var units = scroll.Vertical * _settings.ScrollSpeed * 120 / CrownPixelsPerNotch * sign;
        if (scroll.Horizontal != 0)
            EmitWheel(ref _horizontalAccumulator, scroll.Horizontal * _settings.ScrollSpeed * 120 / CrownPixelsPerNotch * sign, horizontal: true);
        if (scroll.Vertical == 0) return;

        switch (_profile.Crown)
        {
            case CrownMode.Scroll: EmitWheel(ref _scrollAccumulator, units, horizontal: false); break;
            case CrownMode.HorizontalScroll: EmitWheel(ref _scrollAccumulator, units, horizontal: true); break;
            case CrownMode.Zoom:
                EmitSteps(units, 120, up => { _input.Keys([new KeyStroke(VirtualKeys.Control, true)]); _input.Wheel(up ? 120 : -120, false); _input.Keys([new KeyStroke(VirtualKeys.Control, false)]); });
                break;
            case CrownMode.Volume: EmitSteps(units, 60, up => Actions?.RunKeys(up ? "VolumeUp" : "VolumeDown")); break;
            case CrownMode.Slides: EmitSteps(units, 240, up => Actions?.RunKeys(up ? "Left" : "Right")); break;
            case CrownMode.Off: break;
        }
    }

    private void EmitWheel(ref double accumulator, double units, bool horizontal)
    {
        accumulator += units;
        // Smooth: send partial deltas, but at least 1/8 notch at a time to keep the message rate sane.
        var quantum = _settings.SmoothScroll ? 15 : 120;
        if (Math.Abs(accumulator) < quantum) return;
        var whole = (int)(Math.Truncate(accumulator / quantum) * quantum);
        accumulator -= whole;
        _input.Wheel(whole, horizontal);
    }

    private void EmitSteps(double units, double perStep, Action<bool> step)
    {
        _scrollAccumulator += units;
        var count = 0;
        while (Math.Abs(_scrollAccumulator) >= perStep && count++ < 10)
        {
            var up = _scrollAccumulator > 0;
            _scrollAccumulator -= up ? perStep : -perStep;
            step(up);
        }
    }

    // ------------------------------------------------------------------ center, pause, info

    private void OnWatchCenter()
    {
        // The watch zeroed its own heading/elevation, so PC-side offsets no longer apply.
        _offsetLeft = _offsetUp = 0;
        if (_settings.Mode == PointerMode.Relative) MoveToAreaCenter();
        ResetMotion();
        Raise(EngineNotice.Centered);
    }

    /// <summary>PC-side recenter (dashboard button / hotkey): the current aim becomes the middle of the pointing area.
    /// Works with the original watch app too, which has no way to be told to recenter.</summary>
    public void Recenter()
    {
        lock (_gate) RecenterLocked();
        FlushNotices();
    }

    private void RecenterLocked()
    {
        _offsetLeft = _aimLeft;
        _offsetUp = _aimUp;
        if (_settings.Mode == PointerMode.Relative) MoveToAreaCenter();
        ResetMotion();
        Raise(EngineNotice.Centered);
    }

    private void MoveToAreaCenter()
    {
        var area = _displays.Current.PointingArea(_settings.PointingArea, _settings.PointingDisplayId);
        var c = new PointI((int)area.CenterX, (int)area.CenterY);
        _input.MoveTo(c);
        _lastPosted = c;
        _target = _shown = new PointD(c.X, c.Y);
    }

    private void OnWatchPause(bool paused)
    {
        _watchPaused = paused;
        if (paused) Drop(null);
        _phase = paused ? PointerPhase.Paused : PointerPhase.Live;
        _log.LogInformation(paused ? "Watch paused" : "Watch resumed");
        Raise(paused ? EngineNotice.Paused : EngineNotice.Resumed);
    }

    private void OnInfo(WatchInfo info)
    {
        var accepted = AcceptWatch?.Invoke(info) ?? true;
        if (!accepted)
        {
            if (!_untrusted) _log.LogWarning("Ignoring input from untrusted watch {Name}", info.Name);
            _untrusted = true;
            Drop(null);
            Raise(EngineNotice.UntrustedWatch, info.Name);
        }
        else
        {
            _untrusted = false;
        }
        if (_watch?.Model != info.Model) _log.LogInformation("Watch: {Model} ({Id}), watchOS {Os}, protocol v{Proto}", info.ModelName, info.Model, info.Os, info.Proto);
        _watch = info;
        Raise(EngineNotice.WatchInfo);
    }

    // ------------------------------------------------------------------ helpers

    private void ResetMotion()
    {
        _filterX.Reset();
        _filterY.Reset();
        _relative.Reset();
        _velocity = _rawVelocity = _rawAcceleration = default;
        _velocityReference = _rawReference = null;
        if (_settings.Mode == PointerMode.Absolute) _target = null; // next sample starts fresh
        _scrollAccumulator = _horizontalAccumulator = 0;
        _historyCount = 0;
    }

    private void ApplyFilterSettings()
    {
        _filterX.MinCutoff = _filterY.MinCutoff = _settings.SmoothingMinCutoff;
        _filterX.Beta = _filterY.Beta = _settings.SmoothingBeta;
    }

    private void AddHistory(double time, PointD p)
    {
        var index = (_historyStart + _historyCount) % _history.Length;
        _history[index] = (time, p);
        if (_historyCount < _history.Length) _historyCount++;
        else _historyStart = (_historyStart + 1) % _history.Length;
        // Keep about one second, plus the last entry before that window: if the cursor sat still for a while, that
        // entry is where it was, and a click rewinding into the still period must find it.
        while (_historyCount > 1 && time - _history[(_historyStart + 1) % _history.Length].Time > 1)
        {
            _historyStart = (_historyStart + 1) % _history.Length;
            _historyCount--;
        }
    }

    /// <summary>Last position at or before <paramref name="moment"/>; the oldest one if all are later.</summary>
    private PointD HistoryAt(double moment)
    {
        var result = _history[_historyStart].Point;
        for (var i = 0; i < _historyCount; i++)
        {
            var entry = _history[(_historyStart + i) % _history.Length];
            if (entry.Time <= moment) result = entry.Point;
            else break;
        }
        return result;
    }

    private void Raise(EngineNotice notice, string? detail = null) => _pendingNotices.Add(new EngineNoticeArgs(notice, detail));

    private void FlushNotices()
    {
        EngineNoticeArgs[] notices;
        lock (_gate)
        {
            if (_pendingNotices.Count == 0) return;
            notices = [.. _pendingNotices];
            _pendingNotices.Clear();
        }
        foreach (var n in notices)
        {
            try { Notice?.Invoke(n); }
            catch (Exception ex) { _log.LogError(ex, "Notice handler failed for {Notice}", n.Notice); }
        }
    }

    public EngineSnapshot Snapshot()
    {
        lock (_gate)
        {
            var now = _clock.Now;
            _sampleRate.Roll(now);
            _tickRate.Roll(now);
            return new EngineSnapshot
            {
                Phase = _phase,
                PcPaused = _pcPaused,
                Dragging = _dragging,
                Watch = _watch,
                WatchUntrusted = _untrusted,
                Mode = _settings.Mode,
                ProfileName = _profile.Name,
                SampleRate = now - _lastSample < 1.5 ? _sampleRate.Rate : 0,
                TickRate = _tickRate.Rate,
                AimLeft = _aimLeft - _offsetLeft,
                AimUp = _aimUp - _offsetUp,
                Cursor = _lastPosted ?? _input.CursorPosition,
                Packets = _packets,
                InvalidPackets = _invalid,
                InvalidByReason = new Dictionary<DecodeError, long>(_invalidByReason),
                Clicks = _clicks,
                ReorderedSamples = _reordered,
                SecondsSinceLastPacket = double.IsNegativeInfinity(_lastSample) ? double.PositiveInfinity : now - _lastSample,
                LinkExcessDelay = _linkExcess.Summarize(),
                ArrivalInterval = _arrivalInterval.Summarize(),
                PipelineLatency = _pipeline.Summarize(),
                RoundTrip = Probe.RoundTrip,
                TickCost = _tickCost.Summarize(),
            };
        }
    }

    /// <summary>Latest raw aim (before PC-side offsets), for calibration.</summary>
    public (double Left, double Up) RawAim { get { lock (_gate) return (_aimLeft, _aimUp); } }

    public void ResetStatistics()
    {
        lock (_gate)
        {
            _packets = _invalid = _clicks = 0;
            _invalidByReason.Clear();
            _linkExcess.Clear(); _arrivalInterval.Clear(); _pipeline.Clear(); _tickCost.Clear();
        }
        Probe.Reset();
    }
}
