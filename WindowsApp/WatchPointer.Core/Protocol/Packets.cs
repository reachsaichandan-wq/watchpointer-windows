namespace WatchPointer.Core.Protocol;

public enum MouseButton { Left, Right, Middle }

/// <summary>A decoded watch-to-PC packet. <c>WatchMs</c> is the watch's 16-bit millisecond clock (wraps every 65.5 s),
/// or null when an older watch build did not send it.</summary>
public abstract record WatchPacket(PacketKind Kind);

/// <summary>Where the watch's 12 o'clock edge points relative to "Center". Left = turned left, Up = tipped up.</summary>
public sealed record AimPacket(double LeftDegrees, double UpDegrees, ushort? WatchMs) : WatchPacket(PacketKind.Aim);

public sealed record ClickPacket(MouseButton Button, ushort? WatchMs)
    : WatchPacket(Button == MouseButton.Right ? PacketKind.RightClick : PacketKind.LeftClick);

/// <summary>Crown scroll in "Mac pixels" (positive = up). Horizontal is a v2 addition (always 0 from v1 watches).</summary>
public sealed record ScrollPacket(short Vertical, short Horizontal) : WatchPacket(PacketKind.Scroll);

public sealed record CenterPacket : WatchPacket
{
    public CenterPacket() : base(PacketKind.Center) { }
}

public sealed record PausePacket(bool Paused, PauseReason Reason = PauseReason.Button) : WatchPacket(PacketKind.Pause);

public sealed record InfoPacket(WatchInfo Info) : WatchPacket(PacketKind.Info);

/// <summary>Grab = mouse down (hold started), otherwise drop = mouse up. Time is touch-down / just before lift.</summary>
public sealed record DragPacket(bool Grab, ushort? WatchMs) : WatchPacket(PacketKind.Drag);

public sealed record GesturePacket(WatchGesture Gesture, ushort? WatchMs) : WatchPacket(PacketKind.Gesture);

public sealed record PongPacket(ushort Sequence, ushort? WatchMs) : WatchPacket(PacketKind.Pong);

public enum DecodeError
{
    None,
    Empty,
    TooShort,
    TooLong,
    UnknownKind,
    BadInfo,
    OutOfRange,
}
