namespace WatchPointer.Core.Protocol;

/// <summary>First byte of every watch-to-PC packet. 0–7 are protocol v1 (original WatchPointer); 8–9 are v2.</summary>
public enum PacketKind : byte
{
    Aim = 0,
    LeftClick = 1,
    RightClick = 2,
    Scroll = 3,
    Center = 4,
    Pause = 5,
    Info = 6,
    Drag = 7,
    /// <summary>v2: a discrete watch gesture (swipe, system double tap, remote button); a = <see cref="WatchGesture"/>.</summary>
    Gesture = 8,
    /// <summary>v2: reply to a ping notification; a = ping sequence number.</summary>
    Pong = 9,
}

/// <summary>v2 gesture identifiers carried in <see cref="PacketKind.Gesture"/> packets.</summary>
public enum WatchGesture : short
{
    SwipeLeft = 1,
    SwipeRight = 2,
    SwipeUp = 3,
    SwipeDown = 4,
    /// <summary>watchOS system "Double Tap" hand gesture (only on watch models that support it).</summary>
    HandDoubleTap = 5,
    /// <summary>Undirected quick wrist flick (acceleration spike). Older v2 watch builds only.</summary>
    WristFlick = 6,
    /// <summary>Sharp, short turn of the wrist to the left / right and back (yaw-rate spike), opt-in.</summary>
    FlickLeft = 7,
    FlickRight = 8,
    /// <summary>Rapid back-and-forth shake (several acceleration reversals within ~0.7 s).</summary>
    Shake = 9,
    /// <summary>Wrist rolled clockwise / anticlockwise past ~35° and held; repeats while held. Opt-in.</summary>
    TwistClockwise = 10,
    TwistAnticlockwise = 11,
    ButtonA = 16,
    ButtonB = 17,
    ButtonC = 18,
    ButtonD = 19,
    ButtonE = 20,
    ButtonF = 21,
    ButtonG = 22,
    ButtonH = 23,
    /// <summary>Fixed-function watch buttons (not remappable; the PC acts on them directly).</summary>
    PrecisionToggle = 32,
    DragLockToggle = 33,
    NextDisplay = 34,
}

/// <summary>Reason byte (b) of a pause packet, v2. v1 watches always send 0.</summary>
public enum PauseReason : short
{
    Button = 0,
    /// <summary>The watch was lowered (pointing at the floor); resumes when raised.</summary>
    Lowered = 1,
}

/// <summary>v2 PC-to-watch notification types (first byte on the output characteristic).</summary>
public enum NotificationType : byte
{
    Ping = 1,
    State = 2,
}

[Flags]
public enum PcStateFlags : byte
{
    None = 0,
    Paused = 1,
    Laser = 2,
    /// <summary>A precision / fast / drawing speed mode is active (details in the profile characteristic).</summary>
    SpeedMode = 4,
    DragLocked = 8,
}
