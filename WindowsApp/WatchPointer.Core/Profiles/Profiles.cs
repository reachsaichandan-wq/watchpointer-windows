using System.Text.Json.Serialization;

namespace WatchPointer.Core.Profiles;

/// <summary>Watch inputs that can be mapped to actions. All are reliably detectable on the watch:
/// touches on the pad, on-screen buttons, and the watchOS system double tap where the hardware has it.</summary>
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
    HandDoubleTap,
    WristFlick,
    ButtonA,
    ButtonB,
    ButtonC,
    ButtonD,
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

    public static ActionSpec None { get; } = new();
    public static ActionSpec Of(ActionType type) => new() { Type = type };
    public static ActionSpec Shortcut(string keys) => new() { Type = ActionType.Keys, Keys = keys };

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
}

public static class DefaultProfiles
{
    public const string Desktop = "Desktop", Presentation = "Presentation", Media = "Media", Custom = "Custom";

    public static List<ControlProfile> Create() => [CreateDesktop(), CreatePresentation(), CreateMedia(), CreateCustom()];

    /// <summary>Original WatchPointer behaviour plus window management on the swipes and remote buttons.</summary>
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
            [WatchInput.SwipeLeft] = ActionSpec.Shortcut("Ctrl+Win+Right"),   // next virtual desktop
            [WatchInput.SwipeRight] = ActionSpec.Shortcut("Ctrl+Win+Left"),   // previous virtual desktop
            [WatchInput.SwipeUp] = ActionSpec.Shortcut("Win+Tab"),            // Task View
            [WatchInput.SwipeDown] = ActionSpec.Shortcut("Esc"),
            [WatchInput.HandDoubleTap] = ActionSpec.Of(ActionType.DoubleClick),
            [WatchInput.WristFlick] = ActionSpec.Shortcut("Esc"),
            [WatchInput.ButtonA] = ActionSpec.Shortcut("Alt+Tab"),            // switch app
            [WatchInput.ButtonB] = ActionSpec.Shortcut("Enter"),
            [WatchInput.ButtonC] = ActionSpec.Shortcut("Win+Left"),           // snap left
            [WatchInput.ButtonD] = ActionSpec.Shortcut("Win+Right"),          // snap right
        },
    };

    /// <summary>PowerPoint / Keynote-style keys. They go to the focused window, so the slideshow must have focus.</summary>
    public static ControlProfile CreatePresentation() => new()
    {
        Name = Presentation,
        Kind = ProfileKind.Presentation,
        Crown = CrownMode.Slides,
        LaserPointer = true,
        Mappings = new()
        {
            [WatchInput.PadTap] = ActionSpec.Shortcut("Right"),           // next slide
            [WatchInput.PadHold] = ActionSpec.Of(ActionType.Drag),
            [WatchInput.RightButton] = ActionSpec.Shortcut("Left"),       // previous slide
            [WatchInput.SwipeLeft] = ActionSpec.Shortcut("Right"),
            [WatchInput.SwipeRight] = ActionSpec.Shortcut("Left"),
            [WatchInput.SwipeUp] = ActionSpec.Shortcut("F5"),             // start slideshow from the beginning
            [WatchInput.SwipeDown] = ActionSpec.Shortcut("Esc"),          // end slideshow
            [WatchInput.HandDoubleTap] = ActionSpec.Shortcut("Right"),
            [WatchInput.WristFlick] = ActionSpec.Shortcut("Right"),
            [WatchInput.ButtonA] = ActionSpec.Shortcut("F5"),
            [WatchInput.ButtonB] = ActionSpec.Shortcut("Esc"),
            [WatchInput.ButtonC] = ActionSpec.Shortcut("B"),              // black screen (PowerPoint)
            [WatchInput.ButtonD] = ActionSpec.Of(ActionType.ToggleLaser),
        },
    };

    /// <summary>System media keys: Windows routes them to the active media session regardless of focus.</summary>
    public static ControlProfile CreateMedia() => new()
    {
        Name = Media,
        Kind = ProfileKind.Media,
        Crown = CrownMode.Volume,
        MovePointer = true,
        Mappings = new()
        {
            [WatchInput.PadTap] = ActionSpec.Shortcut("MediaPlayPause"),
            [WatchInput.PadHold] = ActionSpec.Of(ActionType.Drag),
            [WatchInput.RightButton] = ActionSpec.Shortcut("VolumeMute"),
            [WatchInput.SwipeLeft] = ActionSpec.Shortcut("MediaNext"),
            [WatchInput.SwipeRight] = ActionSpec.Shortcut("MediaPrevious"),
            [WatchInput.SwipeUp] = ActionSpec.Shortcut("VolumeUp"),
            [WatchInput.SwipeDown] = ActionSpec.Shortcut("VolumeDown"),
            [WatchInput.HandDoubleTap] = ActionSpec.Shortcut("MediaPlayPause"),
            [WatchInput.WristFlick] = ActionSpec.Shortcut("MediaNext"),
            [WatchInput.ButtonA] = ActionSpec.Shortcut("MediaPrevious"),
            [WatchInput.ButtonB] = ActionSpec.Shortcut("MediaPlayPause"),
            [WatchInput.ButtonC] = ActionSpec.Shortcut("MediaNext"),
            [WatchInput.ButtonD] = ActionSpec.Shortcut("VolumeMute"),
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
            [WatchInput.ButtonA] = ActionSpec.Shortcut("Ctrl+C"),
            [WatchInput.ButtonB] = ActionSpec.Shortcut("Ctrl+V"),
            [WatchInput.ButtonC] = ActionSpec.Shortcut("Ctrl+Z"),
            [WatchInput.ButtonD] = ActionSpec.Of(ActionType.NextProfile),
        },
    };

    public static WatchInput? FromGesture(Protocol.WatchGesture g) => g switch
    {
        Protocol.WatchGesture.SwipeLeft => WatchInput.SwipeLeft,
        Protocol.WatchGesture.SwipeRight => WatchInput.SwipeRight,
        Protocol.WatchGesture.SwipeUp => WatchInput.SwipeUp,
        Protocol.WatchGesture.SwipeDown => WatchInput.SwipeDown,
        Protocol.WatchGesture.HandDoubleTap => WatchInput.HandDoubleTap,
        Protocol.WatchGesture.WristFlick => WatchInput.WristFlick,
        Protocol.WatchGesture.ButtonA => WatchInput.ButtonA,
        Protocol.WatchGesture.ButtonB => WatchInput.ButtonB,
        Protocol.WatchGesture.ButtonC => WatchInput.ButtonC,
        Protocol.WatchGesture.ButtonD => WatchInput.ButtonD,
        _ => null,
    };
}
