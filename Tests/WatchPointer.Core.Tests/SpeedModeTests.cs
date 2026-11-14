using WatchPointer.Core.Pointer;
using WatchPointer.Core.Protocol;
using WatchPointer.Core.Session;
using WatchPointer.Core.Settings;

namespace WatchPointer.Core.Tests;

public class SpeedModeTests
{
    private static int X(EngineHarness h) => h.Sink.CursorPosition.X;

    [Fact]
    public void Switching_into_precision_does_not_move_the_cursor()
    {
        var h = new EngineHarness();
        h.Aim(-5, 2, 1.0);
        var before = h.Sink.CursorPosition;
        h.Engine.SpeedMode = SpeedMode.Precision;
        h.Aim(-5, 2, 0.5);
        Assert.InRange(X(h), before.X - 2, before.X + 2);
    }

    [Fact]
    public void Precision_scales_movement_down_and_fast_scales_it_up()
    {
        int Travel(SpeedMode mode)
        {
            var h = new EngineHarness(settings: new PointerSettings { PredictionMs = 0 });
            h.Aim(0, 0, 1.0);
            h.Engine.SpeedMode = mode;
            h.Aim(0, 0, 0.3);
            var start = X(h);
            h.Aim(-5, 0, 1.0);
            return X(h) - start;
        }
        var normal = Travel(SpeedMode.Normal);
        var precision = Travel(SpeedMode.Precision);
        var fast = Travel(SpeedMode.Fast);
        Assert.InRange(precision, normal * 0.30, normal * 0.40);   // 0.35×
        Assert.InRange(fast, normal * 1.9, normal * 2.1);          // 2×
    }

    [Fact]
    public void Leaving_precision_keeps_the_cursor_and_re_anchors_laser_pointing()
    {
        var h = new EngineHarness(settings: new PointerSettings { PredictionMs = 0 });
        h.Aim(0, 0, 1.0);
        h.Engine.SpeedMode = SpeedMode.Precision;
        h.Aim(0, 0, 0.2);
        h.Aim(-10, 0, 1.0);             // fine move: cursor moved only 35 % of the way
        var there = X(h);
        h.Engine.SpeedMode = SpeedMode.Normal;
        h.Aim(-10, 0, 0.5);             // same aim: no jump to the absolute position
        Assert.InRange(X(h), there - 2, there + 2);
        h.Aim(-12, 0, 1.0);             // and normal-speed movement resumes from there
        Assert.True(X(h) > there + 40, $"{X(h)} vs {there}");
    }

    [Fact]
    public void Drawing_mode_never_predicts_past_the_hand()
    {
        var settings = new PointerSettings { DrawingScale = 1.0 };
        var normal = TrackingRun(settings, SpeedMode.Normal);
        var drawing = TrackingRun(settings, SpeedMode.Drawing);
        Assert.True(drawing.Overshoot < 1, $"drawing overshoot {drawing.Overshoot:F1} px");
        Assert.True(normal.Overshoot > drawing.Overshoot);
    }

    private static (double Overshoot, int Dummy) TrackingRun(PointerSettings s, SpeedMode mode)
    {
        var h = new EngineHarness(settings: s);
        h.Aim(10, 0, 1.0);
        h.Engine.SpeedMode = mode;
        h.Aim(10, 0, 0.3);
        var start = 10.0;
        h.Aim(0, 0, 0.25, _ => (start -= 0.8, 0));   // fast move right, then stop dead
        var stopX = X(h);
        var maxX = stopX;
        for (var i = 0; i < 60; i++) { h.Aim(start, 0, 0.01); maxX = Math.Max(maxX, X(h)); }
        var final = X(h);
        return (Math.Max(0, maxX - final), 0);
    }

    [Fact]
    public void Precision_toggle_from_the_watch_and_state_in_snapshot()
    {
        var h = new EngineHarness();
        h.Send(PacketCodec.Encode(PacketKind.Gesture, (short)WatchGesture.PrecisionToggle));
        Assert.Equal(SpeedMode.Precision, h.Engine.Snapshot().SpeedMode);
        Assert.Contains(h.Notices, n => n.Notice == EngineNotice.SpeedModeChanged);
        h.Send(PacketCodec.Encode(PacketKind.Gesture, (short)WatchGesture.PrecisionToggle));
        Assert.Equal(SpeedMode.Normal, h.Engine.Snapshot().SpeedMode);
    }

    [Fact]
    public void Auto_precision_after_holding_steady_and_exit_on_a_fast_move()
    {
        var h = new EngineHarness(settings: new PointerSettings { AutoPrecision = true });
        h.Aim(0, 0, 3.0);
        var s = h.Engine.Snapshot();
        Assert.Equal(SpeedMode.Precision, s.SpeedMode);
        Assert.True(s.AutoPrecisionActive);
        var left = 0.0;
        h.Aim(0, 0, 0.3, _ => (left -= 1.0, 0));       // 100 °/s flick
        Assert.Equal(SpeedMode.Normal, h.Engine.Snapshot().SpeedMode);
    }

    [Fact]
    public void Drag_lock_grabs_then_drops()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 0.5);
        h.Send(PacketCodec.Encode(PacketKind.Gesture, (short)WatchGesture.DragLockToggle));
        Assert.True(h.Engine.Snapshot().DragLocked);
        h.Aim(-8, 0, 0.5);
        h.Send(PacketCodec.Encode(PacketKind.Gesture, (short)WatchGesture.DragLockToggle));
        var s = h.Engine.Snapshot();
        Assert.False(s.Dragging);
        Assert.False(s.DragLocked);
        Assert.Single(h.Sink.Snapshot(), e => e.StartsWith("LeftDown"));
        Assert.Single(h.Sink.Snapshot(), e => e.StartsWith("LeftUp"));
    }

    [Fact]
    public void Lowered_watch_pauses_with_a_reason()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 0.5);
        h.Send(PacketCodec.Encode(PacketKind.Pause, 1, (short)PauseReason.Lowered));
        var s = h.Engine.Snapshot();
        Assert.Equal(PointerPhase.Paused, s.Phase);
        Assert.Equal(PauseReason.Lowered, s.PauseReason);
        h.Send(PacketCodec.Encode(PacketKind.Pause, 0));
        Assert.Equal(PointerPhase.Live, h.Engine.Snapshot().Phase);
    }

    [Fact]
    public void Profile_state_json_roundtrip_and_size_limit()
    {
        var state = new ProfileState
        {
            Profile = "Desktop",
            Mode = "Precision",
            Buttons = Enumerable.Range(0, 8).Select(i => new ButtonLabel { Gesture = 16 + i, Label = new string('x', 60) }).ToList(),
        };
        var bytes = PacketCodec.EncodeProfileState(state);
        Assert.True(bytes.Length <= 512);
        var back = PacketCodec.DecodeProfileState(bytes)!;
        Assert.Equal("Precision", back.Mode);
        Assert.Equal(8, back.Buttons.Count);
    }

    [Fact]
    public void Old_saved_desktop_profile_gains_new_inputs_but_keeps_user_changes()
    {
        var old = Profiles.DefaultProfiles.CreateDesktop() with { Mappings = new() { [Profiles.WatchInput.PadTap] = Profiles.ActionSpec.Shortcut("Enter") } };
        var s = new AppSettings { Profiles = [old] }.Normalize();
        var desktop = s.Profiles.Single(p => p.Name == "Desktop");
        Assert.Equal("Enter", desktop.Resolve(Profiles.WatchInput.PadTap).Keys);                       // kept
        Assert.Equal("Esc", desktop.Resolve(Profiles.WatchInput.Shake).Keys);                           // added
        Assert.Equal(Profiles.ActionType.LockPc, desktop.Resolve(Profiles.WatchInput.ButtonH).Type);    // added
    }
}
