using System.Text;
using System.Text.Json;
using WatchPointer.Core.Protocol;

namespace WatchPointer.Core.Tests;

public class ProtocolTests
{
    private static readonly JsonDocument Vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-vectors.json")));

    public static TheoryData<string> ValidNames() => Names("valid");
    public static TheoryData<string> InvalidNames() => Names("invalid");

    private static TheoryData<string> Names(string section)
    {
        var data = new TheoryData<string>();
        foreach (var v in Vectors.RootElement.GetProperty(section).EnumerateArray()) data.Add(v.GetProperty("name").GetString()!);
        return data;
    }

    private static JsonElement Find(string section, string name) =>
        Vectors.RootElement.GetProperty(section).EnumerateArray().First(v => v.GetProperty("name").GetString() == name);

    private static byte[] Bytes(JsonElement v) =>
        v.TryGetProperty("json", out var json)
            ? [(byte)v.GetProperty("kind").GetInt32(), .. Encoding.UTF8.GetBytes(json.GetString()!)]
            : Convert.FromHexString(v.GetProperty("hex").GetString()!);

    [Fact]
    public void Uuids_match_the_original_watchpointer()
    {
        Assert.Equal(Guid.Parse("6B1E0001-9F4C-4C2A-9B8E-3D2A1C0F5E01"), ProtocolConstants.ServiceUuid);
        Assert.Equal(Guid.Parse("6B1E0002-9F4C-4C2A-9B8E-3D2A1C0F5E01"), ProtocolConstants.InputUuid);
        Assert.Equal(Guid.Parse("6B1E0003-9F4C-4C2A-9B8E-3D2A1C0F5E01"), ProtocolConstants.OutputUuid);
    }

    [Theory]
    [MemberData(nameof(ValidNames))]
    public void Decodes_valid_vector(string name)
    {
        var v = Find("valid", name);
        var expect = v.GetProperty("expect");
        Assert.True(PacketCodec.TryDecode(Bytes(v), out var packet, out var error), $"{name}: {error}");
        Assert.NotNull(packet);
        var kind = expect.GetProperty("kind").GetString();
        ushort? ms = expect.TryGetProperty("ms", out var m) ? (ushort)m.GetInt32() : null;
        int A() => expect.GetProperty("a").GetInt32();
        int B() => expect.GetProperty("b").GetInt32();

        switch (kind)
        {
            case "aim":
                var aim = Assert.IsType<AimPacket>(packet);
                Assert.Equal(A() / 100.0, aim.LeftDegrees, 6);
                Assert.Equal(B() / 100.0, aim.UpDegrees, 6);
                Assert.Equal(ms, aim.WatchMs);
                break;
            case "leftClick" or "rightClick":
                var click = Assert.IsType<ClickPacket>(packet);
                Assert.Equal(kind == "leftClick" ? MouseButton.Left : MouseButton.Right, click.Button);
                Assert.Equal(ms, click.WatchMs);
                break;
            case "scroll":
                var scroll = Assert.IsType<ScrollPacket>(packet);
                Assert.Equal(A(), scroll.Vertical);
                Assert.Equal(B(), scroll.Horizontal);
                break;
            case "center": Assert.IsType<CenterPacket>(packet); break;
            case "pause": Assert.Equal(A() != 0, Assert.IsType<PausePacket>(packet).Paused); break;
            case "drag":
                var drag = Assert.IsType<DragPacket>(packet);
                Assert.Equal(A() != 0, drag.Grab);
                Assert.Equal(ms, drag.WatchMs);
                break;
            case "gesture":
                var g = Assert.IsType<GesturePacket>(packet);
                Assert.Equal((WatchGesture)A(), g.Gesture);
                Assert.Equal(ms, g.WatchMs);
                break;
            case "pong":
                var pong = Assert.IsType<PongPacket>(packet);
                Assert.Equal((ushort)A(), pong.Sequence);
                break;
            case "info":
                var info = Assert.IsType<InfoPacket>(packet).Info;
                Assert.Equal(expect.GetProperty("model").GetString(), info.Model);
                Assert.Equal(expect.GetProperty("os").GetString(), info.Os);
                Assert.Equal(expect.GetProperty("battery").GetInt32(), info.Battery);
                Assert.Equal(expect.GetProperty("charging").GetBoolean(), info.Charging);
                Assert.Equal(expect.GetProperty("name").GetString(), info.Name);
                Assert.Equal(expect.GetProperty("proto").GetInt32(), info.Proto);
                break;
            default: Assert.Fail($"unknown kind {kind}"); break;
        }
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void Rejects_invalid_vector(string name)
    {
        var v = Find("invalid", name);
        Assert.False(PacketCodec.TryDecode(Bytes(v), out var packet, out var error));
        Assert.Null(packet);
        Assert.Equal(Enum.Parse<DecodeError>(v.GetProperty("error").GetString()!), error);
    }

    [Fact]
    public void Encoder_reproduces_fixed_vectors()
    {
        foreach (var v in Vectors.RootElement.GetProperty("valid").EnumerateArray())
        {
            if (v.TryGetProperty("json", out _)) continue;
            var bytes = Bytes(v);
            var e = v.GetProperty("expect");
            ushort? ms = e.TryGetProperty("ms", out var m) ? (ushort)m.GetInt32() : null;
            var encoded = PacketCodec.Encode((PacketKind)bytes[0], (short)e.GetProperty("a").GetInt32(), (short)e.GetProperty("b").GetInt32(), ms);
            Assert.Equal(Convert.ToHexString(bytes), Convert.ToHexString(encoded));
        }
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(10.5, 1050)]
    [InlineData(-5.256, -526)]
    [InlineData(0.125, 13)]
    [InlineData(400.0, short.MaxValue)]
    [InlineData(-400.0, short.MinValue)]
    public void Centidegrees_round_and_clamp_like_the_watch(double degrees, short expected) =>
        Assert.Equal(expected, PacketCodec.ToCentidegrees(degrees));

    [Fact]
    public void Aim_roundtrip_preserves_hundredths()
    {
        var bytes = PacketCodec.EncodeAim(12.34, -56.78, 4321);
        Assert.True(PacketCodec.TryDecode(bytes, out var p, out _));
        var aim = Assert.IsType<AimPacket>(p);
        Assert.Equal(12.34, aim.LeftDegrees, 6);
        Assert.Equal(-56.78, aim.UpDegrees, 6);
        Assert.Equal((ushort)4321, aim.WatchMs);
    }

    [Fact]
    public void Info_roundtrip_and_sanitizes_fields()
    {
        var bytes = PacketCodec.EncodeInfo(new WatchInfo { Model = "Watch7,5", Os = "11", Battery = 250, Name = new string('x', 200), Proto = 2 });
        Assert.True(PacketCodec.TryDecode(bytes, out var p, out _));
        var info = Assert.IsType<InfoPacket>(p).Info;
        Assert.Equal(-1, info.Battery);           // out-of-range battery becomes "unknown"
        Assert.Equal(64, info.Name.Length);       // long names are truncated
        Assert.Equal("Apple Watch Ultra 2", info.ModelName);
    }

    [Fact]
    public void Info_rejects_oversized_json()
    {
        var bytes = new byte[ProtocolConstants.MaxInfoPacketLength + 1];
        bytes[0] = (byte)PacketKind.Info;
        Assert.False(PacketCodec.TryDecode(bytes, out _, out var error));
        Assert.Equal(DecodeError.TooLong, error);
    }

    [Fact]
    public void Extra_trailing_bytes_are_tolerated_for_forward_compatibility()
    {
        var bytes = Convert.FromHexString("01000000003930AABB");
        Assert.True(PacketCodec.TryDecode(bytes, out var p, out _));
        Assert.Equal((ushort)12345, Assert.IsType<ClickPacket>(p).WatchMs);
    }

    [Fact]
    public void Random_garbage_never_throws()
    {
        var random = new Random(42);
        for (var i = 0; i < 20_000; i++)
        {
            var bytes = new byte[random.Next(0, 40)];
            random.NextBytes(bytes);
            PacketCodec.TryDecode(bytes, out _, out _);
        }
    }

    [Fact]
    public void Notifications_match_vectors()
    {
        foreach (var v in Vectors.RootElement.GetProperty("notifications").EnumerateArray())
        {
            var bytes = Convert.FromHexString(v.GetProperty("hex").GetString()!);
            var e = v.GetProperty("expect");
            Assert.True(PacketCodec.TryDecodeNotification(bytes, out var type, out var seq, out var flags, out var profile));
            if (e.GetProperty("type").GetString() == "ping")
            {
                Assert.Equal(NotificationType.Ping, type);
                Assert.Equal(e.GetProperty("seq").GetInt32(), seq);
                Assert.Equal(Convert.ToHexString(bytes), Convert.ToHexString(PacketCodec.EncodePing(seq)));
            }
            else
            {
                Assert.Equal(NotificationType.State, type);
                Assert.Equal(e.GetProperty("flags").GetInt32(), (int)flags);
                Assert.Equal(e.GetProperty("profile").GetString(), profile);
                Assert.Equal(Convert.ToHexString(bytes), Convert.ToHexString(PacketCodec.EncodeState(flags, profile)));
            }
        }
    }

    [Fact]
    public void State_message_fits_in_20_bytes_and_never_splits_utf8()
    {
        var bytes = PacketCodec.EncodeState(PcStateFlags.Paused, "Präsentationsmodus Ü");
        Assert.True(bytes.Length <= 20);
        var name = Encoding.UTF8.GetString(bytes, 2, bytes.Length - 2);
        Assert.DoesNotContain('�', name);
    }

    [Fact]
    public void Pc_info_roundtrip()
    {
        var json = PacketCodec.EncodePcInfo(new PcInfo { Name = "DESKTOP-1", App = "1.0.0" });
        var back = PacketCodec.DecodePcInfo(json);
        Assert.Equal("DESKTOP-1", back!.Name);
        Assert.Equal(ProtocolConstants.ProtocolVersion, back.Proto);
    }
}
