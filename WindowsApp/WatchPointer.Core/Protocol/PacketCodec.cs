using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace WatchPointer.Core.Protocol;

/// <summary>
/// Decodes and encodes WatchPointer packets. Wire format (little endian):
/// <code>[kind:u8][a:i16][b:i16]([ms:u16])</code>, except info: <code>[6][UTF-8 JSON]</code>.
/// Decoding never throws; malformed input returns a <see cref="DecodeError"/> so callers can count and drop it.
/// </summary>
public static class PacketCodec
{
    public static bool TryDecode(ReadOnlySpan<byte> data, out WatchPacket? packet, out DecodeError error)
    {
        packet = null;
        error = DecodeError.None;
        if (data.IsEmpty) { error = DecodeError.Empty; return false; }

        if (data[0] == (byte)PacketKind.Info)
        {
            if (data.Length > ProtocolConstants.MaxInfoPacketLength) { error = DecodeError.TooLong; return false; }
            try
            {
                var info = JsonSerializer.Deserialize(data[1..], ProtocolJsonContext.Default.WatchInfo);
                if (info is null) { error = DecodeError.BadInfo; return false; }
                packet = new InfoPacket(info with
                {
                    Name = Truncate(info.Name, 64),
                    Model = Truncate(info.Model, 32),
                    Os = Truncate(info.Os, 32),
                    Battery = info.Battery is < -1 or > 100 ? -1 : info.Battery,
                    Proto = Math.Max(1, info.Proto),
                });
                return true;
            }
            catch (JsonException) { error = DecodeError.BadInfo; return false; }
        }

        if (data.Length < ProtocolConstants.FixedPacketLength) { error = DecodeError.TooShort; return false; }
        if (data.Length > ProtocolConstants.MaxFixedPacketLength) { error = DecodeError.TooLong; return false; }
        if (data[0] > (byte)PacketKind.Pong) { error = DecodeError.UnknownKind; return false; }

        var kind = (PacketKind)data[0];
        var a = BinaryPrimitives.ReadInt16LittleEndian(data[1..]);
        var b = BinaryPrimitives.ReadInt16LittleEndian(data[3..]);
        ushort? ms = data.Length >= ProtocolConstants.TimedPacketLength ? BinaryPrimitives.ReadUInt16LittleEndian(data[5..]) : null;

        switch (kind)
        {
            case PacketKind.Aim:
                if (Math.Abs((int)a) > ProtocolConstants.MaxAimCentidegrees || Math.Abs((int)b) > ProtocolConstants.MaxAimCentidegrees)
                {
                    error = DecodeError.OutOfRange;
                    return false;
                }
                packet = new AimPacket(a / 100.0, b / 100.0, ms);
                return true;
            case PacketKind.LeftClick: packet = new ClickPacket(MouseButton.Left, ms); return true;
            case PacketKind.RightClick: packet = new ClickPacket(MouseButton.Right, ms); return true;
            case PacketKind.Scroll: packet = new ScrollPacket(a, b); return true;
            case PacketKind.Center: packet = new CenterPacket(); return true;
            case PacketKind.Pause: packet = new PausePacket(a != 0, b == (short)PauseReason.Lowered ? PauseReason.Lowered : PauseReason.Button); return true;
            case PacketKind.Drag: packet = new DragPacket(a != 0, ms); return true;
            case PacketKind.Gesture:
                if (!Enum.IsDefined((WatchGesture)a)) { error = DecodeError.OutOfRange; return false; }
                packet = new GesturePacket((WatchGesture)a, ms);
                return true;
            case PacketKind.Pong: packet = new PongPacket(unchecked((ushort)a), ms); return true;
            default: error = DecodeError.UnknownKind; return false;
        }
    }

    private static string Truncate(string? s, int max) => s is null ? "" : s.Length <= max ? s : s[..max];

    // ---- Encoding (used by the simulator, the console tool and tests; mirrors the watch app) ----

    public static byte[] Encode(PacketKind kind, short a = 0, short b = 0, ushort? ms = null)
    {
        var bytes = new byte[ms is null ? ProtocolConstants.FixedPacketLength : ProtocolConstants.TimedPacketLength];
        bytes[0] = (byte)kind;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(1), a);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(3), b);
        if (ms is { } t) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(5), t);
        return bytes;
    }

    public static byte[] EncodeAim(double leftDegrees, double upDegrees, ushort? ms) =>
        Encode(PacketKind.Aim, ToCentidegrees(leftDegrees), ToCentidegrees(upDegrees), ms);

    public static byte[] EncodeInfo(WatchInfo info)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(info, ProtocolJsonContext.Default.WatchInfo);
        var bytes = new byte[json.Length + 1];
        bytes[0] = (byte)PacketKind.Info;
        json.CopyTo(bytes, 1);
        return bytes;
    }

    /// <summary>Same rounding/clamping as the watch: round to nearest hundredth of a degree, clamp to Int16.</summary>
    public static short ToCentidegrees(double degrees) =>
        (short)Math.Clamp(Math.Round(degrees * 100, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);

    /// <summary>Watch timestamps: milliseconds truncated to 16 bits, like the watch app.</summary>
    public static ushort ToWatchMs(double seconds) => unchecked((ushort)(long)Math.Floor(seconds * 1000));

    // ---- v2 notifications (PC → watch) ----

    public static byte[] EncodePing(ushort sequence)
    {
        var bytes = new byte[3];
        bytes[0] = (byte)NotificationType.Ping;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(1), sequence);
        return bytes;
    }

    /// <summary>State message: [2, flags, UTF-8 profile name (max 17 bytes so the whole message fits in 20)].</summary>
    public static byte[] EncodeState(PcStateFlags flags, string profileName)
    {
        var name = Encoding.UTF8.GetBytes(profileName);
        var length = Math.Min(name.Length, ProtocolConstants.MaxFixedPacketLength - 2);
        // Don't cut a multi-byte UTF-8 sequence in half.
        while (length > 0 && length < name.Length && (name[length] & 0xC0) == 0x80) length--;
        var bytes = new byte[2 + length];
        bytes[0] = (byte)NotificationType.State;
        bytes[1] = (byte)flags;
        Array.Copy(name, 0, bytes, 2, length);
        return bytes;
    }

    public static bool TryDecodeNotification(ReadOnlySpan<byte> data, out NotificationType type, out ushort sequence,
                                             out PcStateFlags flags, out string profile)
    {
        type = default; sequence = 0; flags = PcStateFlags.None; profile = "";
        if (data.Length < 2) return false;
        switch ((NotificationType)data[0])
        {
            case NotificationType.Ping when data.Length >= 3:
                type = NotificationType.Ping;
                sequence = BinaryPrimitives.ReadUInt16LittleEndian(data[1..]);
                return true;
            case NotificationType.State:
                type = NotificationType.State;
                flags = (PcStateFlags)data[1];
                profile = Encoding.UTF8.GetString(data[2..]);
                return true;
            default:
                return false;
        }
    }

    public static byte[] EncodePcInfo(PcInfo info) => JsonSerializer.SerializeToUtf8Bytes(info, ProtocolJsonContext.Default.PcInfo);

    /// <summary>Profile JSON for the watch; kept under 512 bytes (labels are trimmed if needed).</summary>
    public static byte[] EncodeProfileState(ProfileState state)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, ProtocolJsonContext.Default.ProfileState);
        var maxLabel = 14;
        while (bytes.Length > 512 && maxLabel > 3)
        {
            var max = maxLabel--;
            state = state with { Buttons = state.Buttons.Select(b => b with { Label = b.Label.Length > max ? b.Label[..max] : b.Label }).ToList() };
            bytes = JsonSerializer.SerializeToUtf8Bytes(state, ProtocolJsonContext.Default.ProfileState);
        }
        return bytes;
    }

    public static ProfileState? DecodeProfileState(ReadOnlySpan<byte> json)
    {
        try { return JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.ProfileState); }
        catch (JsonException) { return null; }
    }

    public static PcInfo? DecodePcInfo(ReadOnlySpan<byte> json)
    {
        try { return JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.PcInfo); }
        catch (JsonException) { return null; }
    }
}
