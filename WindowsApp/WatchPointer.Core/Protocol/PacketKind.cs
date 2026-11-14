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
    /// <summary>Opt-in quick wrist flick detected from user acceleration on the watch.</summary>
    WristFlick = 6,
    ButtonA = 16,
    ButtonB = 17,
    ButtonC = 18,
    ButtonD = 19,
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
}
