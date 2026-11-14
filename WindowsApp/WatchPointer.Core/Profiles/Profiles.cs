using System.Text.Json.Serialization;
using WatchPointer.Core.Protocol;

namespace WatchPointer.Core.Profiles;

/// <summary>Watch inputs that can be mapped to actions. Only inputs the watch can tell apart reliably: touches on the
/// pad, on-screen buttons, the system Double Tap, and a few distinct motion gestures (shake, flick, twist) that
/// ordinary pointing doesn't produce.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WatchInput>))]
public enum WatchInput
{
    PadTap,
    PadHold,
    RightButton,
    SwipeLeft,
    SwipeRight,
    SwipeUp,
    SwipeDown,
    /// <summary>watchOS system Double Tap (pinch thumb and index finger twice; Series 9 / Ultra 2 and later).</summary>
    HandDoubleTap,
    WristFlick,
    FlickLeft,
    FlickRight,
    Shake,
    TwistClockwise,
    TwistAnticlockwise,
    ButtonA,
    ButtonB,
    ButtonC,
    ButtonD,
    ButtonE,
    ButtonF,
    ButtonG,
    ButtonH,
}

[JsonConverter(typeof(JsonStringEnumConverter<ActionType>))]
public enum ActionType
{
    None,
    LeftClick,
    RightClick,
    MiddleClick,
    DoubleClick,
    /// <summary>Hold the left button while the input is held (only meaningful for <see cref="WatchInput.PadHold"/>).</summary>
    Drag,
    /// <summary>A shortcut or macro string, see <see cref="Input.KeyMacro"/>.</summary>
    Keys,
    Recenter,
    TogglePause,
    ToggleLaser,
    NextProfile,
    SwitchProfile,
    /// <summary>Precision mode on/off (reduced sensitivity for small targets).</summary>
    TogglePrecision,
    /// <summary>Click-and-hold on/off: press once to grab, again to drop.</summary>
    ToggleDragLock,
    /// <summary>Move the pointing area to the next monitor.</summary>
    NextDisplay,
    /// <summary>Lock the PC (LockWorkStation; Win+L can't be injected).</summary>
    LockPc,
    /// <summary>Open the Windows On-Screen Keyboard.</summary>
    ShowKeyboard,
}

[JsonConverter(typeof(JsonStringEnumConverter<CrownMode>))]
public enum CrownMode
{
    Scroll,
    HorizontalScroll,
    /// <summary>Ctrl + wheel (zoom in browsers, Office, image viewers).</summary>
    Zoom,
    Volume,
    /// <summary>Each Crown step presses Right / Left (slides, photos).</summary>
    Slides,
    Off,
}

[JsonConverter(typeof(JsonStringEnumConverter<ProfileKind>))]
public enum ProfileKind { Desktop, Presentation, Media, Custom }

public sealed record ActionSpec
{
    public ActionType Type { get; init; } = ActionType.None;
    /// <summary>For <see cref="ActionType.Keys"/>: shortcut/macro text.</summary>
    public string? Keys { get; init; }
    /// <summary>For <see cref="ActionType.SwitchProfile"/>: target profile name.</summary>
    public string? Profile { get; init; }
    /// <summary>Short name shown on the watch's remote buttons (optional; derived from the action otherwise).</summary>
    public string? Label { get; init; }

    public static ActionSpec None { get; } = new();
    public static ActionSpec Of(ActionType type, string? label = null) => new() { Type = type, Label = label };
    public static ActionSpec Shortcut(string keys, string? label = null) => new() { Type = ActionType.Keys, Keys = keys, Label = label };

    /// <summary>Label for the watch: the explicit one, else a readable name for the action.</summary>
    public string DisplayLabel => !string.IsNullOrWhiteSpace(Label) ? Label! : Type switch
    {
        ActionType.None => "—",
        ActionType.Keys => Keys ?? "",
        ActionType.SwitchProfile => Profile ?? "Profile",
        ActionType.LeftClick => "Click",
        ActionType.RightClick => "Right click",
        ActionType.MiddleClick => "Middle",
        ActionType.DoubleClick => "Double click",
        ActionType.TogglePrecision => "Precision",
        ActionType.ToggleDragLock => "Drag",
        ActionType.NextDisplay => "Display",
        ActionType.LockPc => "Lock PC",
        ActionType.ShowKeyboard => "Keyboard",
        ActionType.ToggleLaser => "Laser",
        ActionType.TogglePause => "Pause",
        _ => Type.ToString(),
    };

    public override string ToString() => Type switch
    {
        ActionType.Keys => Keys ?? "",
        ActionType.SwitchProfile => $"Profile: {Profile}",
        _ => Type.ToString(),
    };
}

public sealed record ControlProfile
{
    public string Name { get; init; } = "Custom";
    public ProfileKind Kind { get; init; } = ProfileKind.Custom;
    public Dictionary<WatchInput, ActionSpec> Mappings { get; init; } = [];
    public CrownMode Crown { get; init; } = CrownMode.Scroll;
    /// <summary>Whether wrist movement moves the cursor in this profile.</summary>
    public bool MovePointer { get; init; } = true;
    /// <summary>Show the on-screen laser dot while this profile is active.</summary>
    public bool LaserPointer { get; init; }

    public ActionSpec Resolve(WatchInput input) => Mappings.TryGetValue(input, out var a) ? a : ActionSpec.None;

    public static readonly WatchInput[] RemoteButtons =
        [WatchInput.ButtonA, WatchInput.ButtonB, WatchInput.ButtonC, WatchInput.ButtonD,
         WatchInput.ButtonE, WatchInput.ButtonF, WatchInput.ButtonG, WatchInput.ButtonH];

    /// <summary>Labels for the watch's remote-button screen.</summary>
    public List<ButtonLabel> ButtonLabels() =>
        RemoteButtons.Select((input, i) => new ButtonLabel { Gesture = (int)WatchGesture.ButtonA + i, Label = Resolve(input).DisplayLabel }).ToList();
}

public static class DefaultProfiles
{
    public const string Desktop = "Desktop", Presentation = "Presentation", Media = "Media", Custom = "Custom";

    public static List<ControlProfile> Create() => [CreateDesktop(), CreatePresentation(), CreateMedia(), CreateCustom()];

    /// <summary>Everyday use: mouse on the pad, window management on swipes and remote buttons.</summary>
    public static ControlProfile CreateDesktop() => new()
    {
        Name = Desktop,
        Kind = ProfileKind.Desktop,
        Crown = CrownMode.Scroll,
        Mappings = new()
        {
            [WatchInput.PadTap] = ActionSpec.Of(ActionType.LeftClick),
            [WatchInput.PadHold] = ActionSpec.Of(ActionType.Drag),
            [WatchInput.RightButton] = ActionSpec.Of(ActionType.RightClick),
            [WatchInput.SwipeLeft] = ActionSpec.Shortcut("Ctrl+Win+Right", "Next desktop"),
            [WatchInput.SwipeRight] = ActionSpec.Shortcut("Ctrl+Win+Left", "Prev desktop"),
            [WatchInput.SwipeUp] = ActionSpec.Shortcut("Win+Tab", "Task View"),
            [WatchInput.SwipeDown] = ActionSpec.Shortcut("Win+D", "Desktop"),
            [WatchInput.HandDoubleTap] = ActionSpec.Shortcut("MediaPlayPause", "Play/Pause"),
            [WatchInput.WristFlick] = ActionSpec.Shortcut("Esc"),
            [WatchInput.FlickLeft] = ActionSpec.Shortcut("Ctrl+Shift+Tab", "Prev tab"),
            [WatchInput.FlickRight] = ActionSpec.Shortcut("Ctrl+Tab", "Next tab"),
            [WatchInput.Shake] = ActionSpec.Shortcut("Esc"),
            [WatchInput.TwistClockwise] = ActionSpec.Shortcut("VolumeUp", "Vol +"),
            [WatchInput.TwistAnticlockwise] = ActionSpec.Shortcut("VolumeDown", "Vol −"),
            [WatchInput.ButtonA] = ActionSpec.Shortcut("Win+D", "Desktop"),
            [WatchInput.ButtonB] = ActionSpec.Shortcut("Alt+Tab", "App switch"),
            [WatchInput.ButtonC] = ActionSpec.Shortcut("Win+Tab", "Task View"),
            [WatchInput.ButtonD] = ActionSpec.Shortcut("Win+Shift+S", "Screenshot"),
            [WatchInput.ButtonE] = ActionSpec.Shortcut("Esc"),
            [WatchInput.ButtonF] = ActionSpec.Shortcut("Enter"),
            [WatchInput.ButtonG] = ActionSpec.Of(ActionType.ShowKeyboard, "Keyboard"),
            [WatchInput.ButtonH] = ActionSpec.Of(ActionType.LockPc, "Lock PC"),
        },
    };

    /// <summary>PowerPoint keys. They go to the focused window, so the slideshow must have focus.</summary>
    public static ControlProfile CreatePresentation() => new()
    {
        Name = Presentation,
        Kind = ProfileKind.Presentation,
        Crown = CrownMode.Slides,
        LaserPointer = true,
        Mappings = new()
        {
            [WatchInput.PadTap] = ActionSpec.Shortcut("Right", "Next"),
            [WatchInput.PadHold] = ActionSpec.Of(ActionType.Drag),
            [WatchInput.RightButton] = ActionSpec.Shortcut("Left", "Previous"),
            [WatchInput.SwipeLeft] = ActionSpec.Shortcut("Right", "Next"),
            [WatchInput.SwipeRight] = ActionSpec.Shortcut("Left", "Previous"),
            [WatchInput.SwipeUp] = ActionSpec.Shortcut("F5", "Start"),
            [WatchInput.SwipeDown] = ActionSpec.Shortcut("Esc", "End"),
            [WatchInput.HandDoubleTap] = ActionSpec.Shortcut("Right", "Next"),
            [WatchInput.WristFlick] = ActionSpec.Shortcut("Right", "Next"),
            [WatchInput.FlickLeft] = ActionSpec.Shortcut("Left", "Previous"),
            [WatchInput.FlickRight] = ActionSpec.Shortcut("Right", "Next"),
            [WatchInput.Shake] = ActionSpec.Shortcut("Esc", "End"),
            [WatchInput.ButtonA] = ActionSpec.Shortcut("F5", "Start"),
            [WatchInput.ButtonB] = ActionSpec.Shortcut("Shift+F5", "From here"),
            [WatchInput.ButtonC] = ActionSpec.Shortcut("Esc", "End show"),
            [WatchInput.ButtonD] = ActionSpec.Shortcut("B", "Black"),
            [WatchInput.ButtonE] = ActionSpec.Shortcut("W", "White"),
            [WatchInput.ButtonF] = ActionSpec.Shortcut("Ctrl+L", "PPT laser"),
            [WatchInput.ButtonG] = ActionSpec.Of(ActionType.ToggleLaser, "Laser dot"),
            [WatchInput.ButtonH] = ActionSpec.Shortcut("Ctrl+P", "Pen"),
        },
    };

    /// <summary>System media keys: Windows routes them to the active media session regardless of focus.
    /// Seek uses arrow keys, which most players (YouTube, Netflix, VLC, Media Player) accept when focused.</summary>
    public static ControlProfile CreateMedia() => new()
    {
        Name = Media,
        Kind = ProfileKind.Media,
        Crown = CrownMode.Volume,
        Mappings = new()
        {
            [WatchInput.PadTap] = ActionSpec.Shortcut("MediaPlayPause", "Play/Pause"),
            [WatchInput.PadHold] = ActionSpec.Of(ActionType.Drag),
            [WatchInput.RightButton] = ActionSpec.Shortcut("VolumeMute", "Mute"),
            [WatchInput.SwipeLeft] = ActionSpec.Shortcut("MediaNext", "Next"),
            [WatchInput.SwipeRight] = ActionSpec.Shortcut("MediaPrevious", "Previous"),
            [WatchInput.SwipeUp] = ActionSpec.Shortcut("VolumeUp", "Vol +"),
            [WatchInput.SwipeDown] = ActionSpec.Shortcut("VolumeDown", "Vol −"),
            [WatchInput.HandDoubleTap] = ActionSpec.Shortcut("MediaPlayPause", "Play/Pause"),
            [WatchInput.WristFlick] = ActionSpec.Shortcut("MediaNext", "Next"),
            [WatchInput.FlickLeft] = ActionSpec.Shortcut("MediaPrevious", "Previous"),
            [WatchInput.FlickRight] = ActionSpec.Shortcut("MediaNext", "Next"),
            [WatchInput.Shake] = ActionSpec.Shortcut("Esc"),
            [WatchInput.TwistClockwise] = ActionSpec.Shortcut("VolumeUp", "Vol +"),
            [WatchInput.TwistAnticlockwise] = ActionSpec.Shortcut("VolumeDown", "Vol −"),
            [WatchInput.ButtonA] = ActionSpec.Shortcut("MediaPrevious", "Previous"),
            [WatchInput.ButtonB] = ActionSpec.Shortcut("MediaPlayPause", "Play/Pause"),
            [WatchInput.ButtonC] = ActionSpec.Shortcut("MediaNext", "Next"),
            [WatchInput.ButtonD] = ActionSpec.Shortcut("VolumeMute", "Mute"),
            [WatchInput.ButtonE] = ActionSpec.Shortcut("Left", "Seek −"),
            [WatchInput.ButtonF] = ActionSpec.Shortcut("Right", "Seek +"),
            [WatchInput.ButtonG] = ActionSpec.Shortcut("F", "Fullscreen"),
            [WatchInput.ButtonH] = ActionSpec.Shortcut("MediaStop", "Stop"),
        },
    };

    public static ControlProfile CreateCustom() => new()
    {
        Name = Custom,
        Kind = ProfileKind.Custom,
        Crown = CrownMode.Scroll,
        Mappings = new()
        {
            [WatchInput.PadTap] = ActionSpec.Of(ActionType.LeftClick),
            [WatchInput.PadHold] = ActionSpec.Of(ActionType.Drag),
            [WatchInput.RightButton] = ActionSpec.Of(ActionType.RightClick),
            [WatchInput.Shake] = ActionSpec.Shortcut("Esc"),
            [WatchInput.ButtonA] = ActionSpec.Shortcut("Ctrl+C", "Copy"),
            [WatchInput.ButtonB] = ActionSpec.Shortcut("Ctrl+V", "Paste"),
            [WatchInput.ButtonC] = ActionSpec.Shortcut("Ctrl+Z", "Undo"),
            [WatchInput.ButtonD] = ActionSpec.Of(ActionType.NextProfile, "Next profile"),
        },
    };

    public static WatchInput? FromGesture(WatchGesture g) => g switch
    {
        WatchGesture.SwipeLeft => WatchInput.SwipeLeft,
        WatchGesture.SwipeRight => WatchInput.SwipeRight,
        WatchGesture.SwipeUp => WatchInput.SwipeUp,
        WatchGesture.SwipeDown => WatchInput.SwipeDown,
        WatchGesture.HandDoubleTap => WatchInput.HandDoubleTap,
        WatchGesture.WristFlick => WatchInput.WristFlick,
        WatchGesture.FlickLeft => WatchInput.FlickLeft,
        WatchGesture.FlickRight => WatchInput.FlickRight,
        WatchGesture.Shake => WatchInput.Shake,
        WatchGesture.TwistClockwise => WatchInput.TwistClockwise,
        WatchGesture.TwistAnticlockwise => WatchInput.TwistAnticlockwise,
        >= WatchGesture.ButtonA and <= WatchGesture.ButtonH => WatchInput.ButtonA + (g - WatchGesture.ButtonA),
        _ => null,
    };

    /// <summary>Fixed-function watch buttons, not remappable.</summary>
    public static ActionType? FixedAction(WatchGesture g) => g switch
    {
        WatchGesture.PrecisionToggle => ActionType.TogglePrecision,
        WatchGesture.DragLockToggle => ActionType.ToggleDragLock,
        WatchGesture.NextDisplay => ActionType.NextDisplay,
        _ => null,
    };
}
