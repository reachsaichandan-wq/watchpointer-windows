using WatchPointer.Core.Input;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Profiles;
using WatchPointer.Core.Protocol;
using WatchPointer.Core.Session;

namespace WatchPointer.Core.Tests;

public class PointerEngineTests
{
    [Fact]
    public void Aiming_at_center_moves_cursor_to_center()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 0.5);
        Assert.Equal(new PointI(960, 540), h.Sink.CursorPosition);
        Assert.Equal(PointerPhase.Live, h.Engine.Snapshot().Phase);
        Assert.Contains(h.Notices, n => n.Notice == EngineNotice.Connected);
    }

    [Fact]
    public void Turning_right_moves_toward_right_edge_and_settles()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 0.3);
        h.Aim(-12.5, 0, 2.0);
        var expected = AbsoluteProjector.Project(-12.5, 0, RectI.FromSize(0, 0, 1920, 1080), new PointerSettings());
        Assert.InRange(h.Sink.CursorPosition.X, expected.X - 2, expected.X + 2);
    }

    [Fact]
    public void Cursor_never_leaves_the_desktop()
    {
        var h = new EngineHarness();
        h.Aim(-70, 60, 1.0);
        Assert.Equal(new PointI(1919, 0), h.Sink.CursorPosition);
    }

    [Fact]
    public void Smoothing_keeps_a_trembling_hand_steady()
    {
        var h = new EngineHarness();
        var random = new Random(3);
        h.Aim(0, 0, 0.5);
        var xs = new List<int>();
        h.Aim(0, 0, 2.0, _ => ((random.NextDouble() - 0.5) * 0.3, (random.NextDouble() - 0.5) * 0.3));
        for (var i = 0; i < 100; i++) { h.Aim(0, 0, 0.01, _ => ((random.NextDouble() - 0.5) * 0.3, 0)); xs.Add(h.Sink.CursorPosition.X); }
        // ±0.15° of raw jitter is ±6 px raw at speed 1; filtered it should stay within a few pixels.
        Assert.True(xs.Max() - xs.Min() <= 4, $"spread {xs.Max() - xs.Min()} px");
    }

    [Fact]
    public void Click_lands_where_the_pointer_was_at_touch_down()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 1.0);                         // steady on center
        var touchDown = h.Ms;
        h.Aim(-10, 0, 0.15);                      // the tap's jolt swings the aim right…
        h.Send(PacketCodec.Encode(PacketKind.LeftClick, 0, 0, touchDown)); // …but the click carries touch-down time
        var down = h.Sink.Snapshot().First(e => e.StartsWith("LeftDown"));
        Assert.Equal("LeftDown@960,540", down);
        Assert.Contains("LeftUp@960,540", h.Sink.Snapshot());
    }

    [Fact]
    public void Legacy_click_without_time_rewinds_the_fallback()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 1.0);
        h.Aim(-10, 0, 0.05);                      // moved just 50 ms ago
        h.Send(PacketCodec.Encode(PacketKind.LeftClick));
        var down = h.Sink.Snapshot().First(e => e.StartsWith("LeftDown"));
        Assert.Equal("LeftDown@960,540", down);   // 100 ms rewind reaches back before the move
    }

    [Fact]
    public void Right_click_uses_the_right_button()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 0.5);
        h.Send(PacketCodec.Encode(PacketKind.RightClick, 0, 0, h.Ms));
        Assert.Contains(h.Sink.Snapshot(), e => e.StartsWith("RightDown"));
        Assert.Contains(h.Sink.Snapshot(), e => e.StartsWith("RightUp"));
    }

    [Fact]
    public void Two_quick_taps_become_a_windows_double_click_at_the_same_spot()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 1.0);
        h.Send(PacketCodec.Encode(PacketKind.LeftClick, 0, 0, h.Ms));
        h.Aim(0, 0, 0.12);
        h.Send(PacketCodec.Encode(PacketKind.LeftClick, 0, 0, h.Ms));
        var downs = h.Sink.Snapshot().Where(e => e.StartsWith("LeftDown")).ToList();
        Assert.Equal(2, downs.Count);
        Assert.Equal(downs[0], downs[1]);
    }

    [Fact]
    public void Hold_drags_and_release_drops()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 1.0);
        h.Send(PacketCodec.Encode(PacketKind.Drag, 1, 0, h.Ms));
        Assert.True(h.Engine.Snapshot().Dragging);
        h.Aim(-10, 0, 1.0);                         // drag to the right
        h.Send(PacketCodec.Encode(PacketKind.Drag, 0, 0, h.Ms));
        var events = h.Sink.Snapshot();
        Assert.Equal("LeftDown@960,540", events.First(e => e.StartsWith("LeftDown")));
        var up = events.Last(e => e.StartsWith("LeftUp"));
        Assert.True(int.Parse(up.Split('@')[1].Split(',')[0]) > 1200, up);
        Assert.False(h.Engine.Snapshot().Dragging);
    }

    [Fact]
    public void Silence_mid_drag_releases_the_button()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 0.5);
        h.Send(PacketCodec.Encode(PacketKind.Drag, 1, 0, h.Ms));
        h.Idle(0.5);
        Assert.False(h.Engine.Snapshot().Dragging);
        Assert.Contains(h.Sink.Snapshot(), e => e.StartsWith("LeftUp"));
    }

    [Fact]
    public void Silence_marks_the_watch_idle_then_new_samples_resume()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 0.5);
        h.Idle(2);
        Assert.Equal(PointerPhase.Idle, h.Engine.Snapshot().Phase);
        Assert.Contains(h.Notices, n => n.Notice == EngineNotice.Lost);
        h.Aim(5, 0, 0.3);
        Assert.Equal(PointerPhase.Live, h.Engine.Snapshot().Phase);
    }

    [Fact]
    public void After_a_gap_the_cursor_jumps_instead_of_gliding_from_the_old_spot()
    {
        var h = new EngineHarness();
        h.Aim(20, 0, 1.0);                       // far left
        h.Idle(0.5);
        h.Sink.Clear();
        h.Aim(-20, 0, 0.02);                     // first samples after the gap: far right
        var expected = AbsoluteProjector.Project(-20, 0, RectI.FromSize(0, 0, 1920, 1080), new PointerSettings());
        Assert.InRange(h.Sink.CursorPosition.X, expected.X - 3, expected.X + 3);
        Assert.True(h.Sink.Moves <= 2, $"glided over {h.Sink.Moves} moves");
    }

    [Fact]
    public void Watch_pause_stops_and_resume_continues()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 0.5);
        h.Send(PacketCodec.Encode(PacketKind.Pause, 1));
        Assert.Equal(PointerPhase.Paused, h.Engine.Snapshot().Phase);
        h.Send(PacketCodec.Encode(PacketKind.Pause, 0));
        Assert.Equal(PointerPhase.Live, h.Engine.Snapshot().Phase);
    }

    [Fact]
    public void Pc_pause_leaves_the_cursor_alone()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 0.5);
        h.Engine.PcPaused = true;
        h.Sink.Clear();
        h.Aim(-15, 5, 0.5);
        Assert.Equal(0, h.Sink.Moves);
        h.Engine.PcPaused = false;
        h.Aim(-15, 5, 0.5);
        Assert.True(h.Sink.Moves > 0);
    }

    [Fact]
    public void Pc_recenter_makes_the_current_aim_the_middle()
    {
        var h = new EngineHarness();
        h.Aim(8, -3, 0.5);
        h.Engine.Recenter();
        h.Aim(8, -3, 0.5);
        Assert.Equal(new PointI(960, 540), h.Sink.CursorPosition);
        Assert.Contains(h.Notices, n => n.Notice == EngineNotice.Centered);
    }

    [Fact]
    public void Watch_center_clears_pc_offsets()
    {
        var h = new EngineHarness();
        h.Aim(8, 0, 0.5);
        h.Engine.Recenter();
        h.Send(PacketCodec.Encode(PacketKind.Center));   // the watch zeroed itself: its next aims are relative to it
        h.Aim(0, 0, 0.5);
        Assert.Equal(new PointI(960, 540), h.Sink.CursorPosition);
    }

    [Fact]
    public void Edge_reanchoring_brings_the_cursor_back_immediately()
    {
        var h = new EngineHarness(settings: new PointerSettings { ReanchorAtEdges = true });
        h.Aim(0, 0, 0.5);
        h.Aim(-40, 0, 0.5);                       // way past the right edge (edge is at -25°)
        h.Aim(-35, 0, 0.5);                       // turn back 5°
        Assert.True(h.Sink.CursorPosition.X < 1800, $"x {h.Sink.CursorPosition.X}");

        var original = new EngineHarness();
        original.Aim(0, 0, 0.5);
        original.Aim(-40, 0, 0.5);
        original.Aim(-35, 0, 0.5);
        Assert.Equal(1919, original.Sink.CursorPosition.X); // original behaviour: still pinned
    }

    [Fact]
    public void Primary_projection_extends_onto_a_second_monitor()
    {
        var layout = new DisplayLayout([
            new DisplayInfo("A", "A", RectI.FromSize(0, 0, 1920, 1080), RectI.FromSize(0, 0, 1920, 1080), true, 1),
            new DisplayInfo("B", "B", RectI.FromSize(1920, 0, 1920, 1080), RectI.FromSize(1920, 0, 1920, 1080), false, 1),
        ]);
        var h = new EngineHarness(layout);
        h.Aim(-40, 0, 1.0);
        Assert.InRange(h.Sink.CursorPosition.X, 1920, 3839);
    }

    [Fact]
    public void All_displays_area_centers_on_the_virtual_desktop()
    {
        var layout = new DisplayLayout([
            new DisplayInfo("A", "A", RectI.FromSize(0, 0, 1920, 1080), RectI.FromSize(0, 0, 1920, 1080), true, 1),
            new DisplayInfo("B", "B", RectI.FromSize(1920, 0, 1920, 1080), RectI.FromSize(1920, 0, 1920, 1080), false, 1),
        ]);
        var h = new EngineHarness(layout, new PointerSettings { PointingArea = PointingArea.AllDisplays });
        h.Aim(0, 0, 0.5);
        Assert.Equal(new PointI(1920, 540), h.Sink.CursorPosition);
    }

    [Fact]
    public void Relative_mode_moves_incrementally_from_the_current_cursor()
    {
        var h = new EngineHarness(settings: new PointerSettings { Mode = PointerMode.Relative, RelativeAcceleration = 0 });
        h.Sink.MoveTo(new PointI(100, 100));
        h.Aim(30, 10, 0.3);                       // absolute angles don't matter in relative mode
        Assert.InRange(h.Sink.CursorPosition.X, 95, 105);
        var start = 30.0;
        h.Aim(0, 0, 0.5, t => (start -= 0.05, 10)); // turn right 5 °/s… then 50 steps
        Assert.True(h.Sink.CursorPosition.X > 150, $"x {h.Sink.CursorPosition.X}");
        Assert.InRange(h.Sink.CursorPosition.Y, 90, 110);
    }

    [Fact]
    public void Relative_mode_holding_still_does_not_drift()
    {
        var h = new EngineHarness(settings: new PointerSettings { Mode = PointerMode.Relative });
        h.Sink.MoveTo(new PointI(500, 500));
        var random = new Random(9);
        h.Aim(0, 0, 3, _ => ((random.NextDouble() - 0.5) * 0.02, (random.NextDouble() - 0.5) * 0.02));
        Assert.InRange(h.Sink.CursorPosition.X, 497, 503);
        Assert.InRange(h.Sink.CursorPosition.Y, 497, 503);
    }

    [Fact]
    public void Scroll_accumulates_crown_turns_into_wheel_deltas()
    {
        var h = new EngineHarness(settings: new PointerSettings { SmoothScroll = false });
        h.Aim(0, 0, 0.2);
        for (var i = 0; i < 10; i++) h.Send(PacketCodec.Encode(PacketKind.Scroll, 5)); // 50 px total = 1 notch
        Assert.Equal(["Wheel:120"], h.Sink.Snapshot().Where(e => e.Contains("Wheel")).ToArray());
    }

    [Fact]
    public void Smooth_scroll_sends_partial_deltas_and_honours_inversion()
    {
        var h = new EngineHarness(settings: new PointerSettings { InvertScroll = true });
        h.Aim(0, 0, 0.2);
        h.Send(PacketCodec.Encode(PacketKind.Scroll, 10)); // 24 units
        var wheel = h.Sink.Snapshot().Single(e => e.StartsWith("Wheel"));
        Assert.Equal("Wheel:-15", wheel);
    }

    [Fact]
    public void Horizontal_scroll_v2()
    {
        var h = new EngineHarness(settings: new PointerSettings { SmoothScroll = false });
        h.Send(PacketCodec.Encode(PacketKind.Scroll, 0, 100));
        Assert.Contains("HWheel:240", h.Sink.Snapshot());
    }

    [Fact]
    public void Invalid_packets_are_counted_by_reason_and_ignored()
    {
        var h = new EngineHarness();
        h.Send([]);
        h.Send([0xFF, 0, 0, 0, 0]);
        h.Send([0x00, 0x01]);
        var s = h.Engine.Snapshot();
        Assert.Equal(3, s.InvalidPackets);
        Assert.Equal(1, s.InvalidByReason[DecodeError.UnknownKind]);
        Assert.Equal(0, h.Sink.Moves);
    }

    [Fact]
    public void Out_of_order_samples_are_dropped()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 0.5);
        var stale = (ushort)(h.Ms - 50);
        h.Send(PacketCodec.EncodeAim(-20, 0, stale));
        Assert.Equal(1, h.Engine.Snapshot().ReorderedSamples);
    }

    [Fact]
    public void Disconnect_drops_drag_and_returns_to_waiting()
    {
        var h = new EngineHarness();
        h.Send(PacketCodec.EncodeInfo(new WatchInfo { Model = "Watch7,5", Name = "W", Battery = 50 }));
        h.Aim(0, 0, 0.5);
        h.Send(PacketCodec.Encode(PacketKind.Drag, 1, 0, h.Ms));
        h.Engine.WatchDisconnected();
        var s = h.Engine.Snapshot();
        Assert.Equal(PointerPhase.Waiting, s.Phase);
        Assert.Null(s.Watch);
        Assert.False(s.Dragging);
        Assert.Contains(h.Sink.Snapshot(), e => e.StartsWith("LeftUp"));
        Assert.Contains(h.Notices, n => n.Notice == EngineNotice.Disconnected);
    }

    [Fact]
    public void Untrusted_watch_input_is_ignored()
    {
        var h = new EngineHarness();
        h.Engine.AcceptWatch = info => info.Name == "Mine";
        h.Send(PacketCodec.EncodeInfo(new WatchInfo { Name = "Someone else" }));
        h.Aim(0, 0, 0.5);
        h.Send(PacketCodec.Encode(PacketKind.LeftClick));
        Assert.Equal(0, h.Sink.Moves);
        Assert.True(h.Engine.Snapshot().WatchUntrusted);
        h.Send(PacketCodec.EncodeInfo(new WatchInfo { Name = "Mine" }));
        h.Aim(0, 0, 0.5);
        Assert.True(h.Sink.Moves > 0);
    }

    [Fact]
    public void Pong_feeds_the_latency_probe()
    {
        var h = new EngineHarness();
        var seq = h.Engine.Probe.NextPing(h.Clock.Now);
        h.Clock.Advance(0.024);
        h.Send(PacketCodec.Encode(PacketKind.Pong, (short)seq, 0, h.Ms));
        var rtt = h.Engine.Snapshot().RoundTrip;
        Assert.Equal(1, rtt.Count);
        Assert.Equal(0.024, rtt.P50, 6);
    }

    [Fact]
    public void Pipeline_latency_is_measured_and_small()
    {
        var h = new EngineHarness();
        h.Aim(0, 0, 0.3);
        h.Aim(-10, 3, 1.0, t => (-10 * Math.Sin(t), 3));
        var s = h.Engine.Snapshot();
        Assert.True(s.PipelineLatency.HasData);
        Assert.True(s.PipelineLatency.P95 <= 1.0 / 240 + 1e-9, $"p95 {s.PipelineLatency.P95 * 1000:F2} ms");
        Assert.InRange(s.SampleRate, 90, 110);
    }
}

public class GestureMappingTests
{
    private static EngineHarness WithRunner(out ActionRunner runner, ControlProfile? profile = null)
    {
        var h = new EngineHarness();
        runner = new ActionRunner(h.Sink);
        h.Engine.Actions = runner;
        if (profile is not null) h.Engine.Profile = profile;
        return h;
    }

    [Fact]
    public async Task Desktop_swipe_up_opens_task_view()
    {
        var h = WithRunner(out var runner);
        h.Send(PacketCodec.Encode(PacketKind.Gesture, (short)WatchGesture.SwipeUp));
        await runner.DrainAsync();
        Assert.Equal(["KeyDown:Win", "KeyDown:Tab", "KeyUp:Tab", "KeyUp:Win"], h.Sink.Snapshot());
        await runner.DisposeAsync();
    }

    [Fact]
    public async Task Presentation_tap_is_next_slide_not_a_click()
    {
        var h = WithRunner(out var runner, DefaultProfiles.CreatePresentation());
        h.Aim(0, 0, 0.5);
        h.Send(PacketCodec.Encode(PacketKind.LeftClick, 0, 0, h.Ms));
        await runner.DrainAsync();
        var events = h.Sink.Snapshot();
        Assert.DoesNotContain(events, e => e.StartsWith("LeftDown"));
        Assert.Equal(["KeyDown:Right", "KeyUp:Right"], events);
        await runner.DisposeAsync();
    }

    [Fact]
    public async Task Media_profile_uses_system_media_keys_and_crown_volume()
    {
        var h = WithRunner(out var runner, DefaultProfiles.CreateMedia());
        h.Send(PacketCodec.Encode(PacketKind.Gesture, (short)WatchGesture.SwipeLeft));
        for (var i = 0; i < 5; i++) h.Send(PacketCodec.Encode(PacketKind.Scroll, 5)); // 25 px = 60 units = one volume step
        await runner.DrainAsync();
        var events = h.Sink.Snapshot();
        Assert.Contains("KeyDown:MediaNext", events);
        Assert.Contains("KeyDown:VolumeUp", events);
        await runner.DisposeAsync();
    }

    [Fact]
    public async Task Hold_with_a_non_drag_action_fires_once_on_press()
    {
        var profile = DefaultProfiles.CreateCustom();
        profile.Mappings[WatchInput.PadHold] = ActionSpec.Shortcut("Esc");
        var h = WithRunner(out var runner, profile);
        h.Send(PacketCodec.Encode(PacketKind.Drag, 1));
        h.Send(PacketCodec.Encode(PacketKind.Drag, 0));
        await runner.DrainAsync();
        Assert.Equal(["KeyDown:Esc", "KeyUp:Esc"], h.Sink.Snapshot());
        await runner.DisposeAsync();
    }

    [Fact]
    public async Task App_level_actions_are_forwarded()
    {
        var h = WithRunner(out var runner, DefaultProfiles.CreatePresentation());
        var requested = new List<AppAction>();
        runner.AppActionRequested += (a, _) => requested.Add(a);
        h.Send(PacketCodec.Encode(PacketKind.Gesture, (short)WatchGesture.ButtonD));
        Assert.Equal([AppAction.ToggleLaser], requested);
        await runner.DisposeAsync();
    }

    [Fact]
    public void Unmapped_input_does_nothing()
    {
        var h = new EngineHarness { };
        h.Engine.Profile = new ControlProfile { Name = "Empty" };
        h.Send(PacketCodec.Encode(PacketKind.LeftClick));
        h.Send(PacketCodec.Encode(PacketKind.Gesture, (short)WatchGesture.HandDoubleTap));
        Assert.Empty(h.Sink.Snapshot());
    }

    [Fact]
    public void Double_click_action()
    {
        var h = new EngineHarness();
        h.Send(PacketCodec.Encode(PacketKind.Gesture, (short)WatchGesture.HandDoubleTap)); // desktop default: double click
        Assert.Equal(2, h.Sink.Snapshot().Count(e => e.StartsWith("LeftDown")));
    }
}
