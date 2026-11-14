namespace WatchPointer.Core.Protocol;

/// <summary>
/// GATT identifiers and limits. The service and the input/output characteristic UUIDs are identical to the
/// original macOS WatchPointer, so the original watch app works with this receiver unchanged (protocol v1).
/// See Shared/Protocol/PROTOCOL.md.
/// </summary>
public static class ProtocolConstants
{
    public static readonly Guid ServiceUuid = Guid.Parse("6B1E0001-9F4C-4C2A-9B8E-3D2A1C0F5E01");

    /// <summary>Watch to PC. Write and write-without-response. Every packet the watch sends goes here.</summary>
    public static readonly Guid InputUuid = Guid.Parse("6B1E0002-9F4C-4C2A-9B8E-3D2A1C0F5E01");

    /// <summary>PC to watch notifications. The watch subscribes on connect; (un)subscribing marks (dis)connection.
    /// v1 sends nothing on it; v2 sends small ping/state messages only to watches that report proto 2 or later.</summary>
    public static readonly Guid OutputUuid = Guid.Parse("6B1E0003-9F4C-4C2A-9B8E-3D2A1C0F5E01");

    /// <summary>v2 addition: readable JSON describing the PC (name, OS, protocol version). Ignored by v1 watches.</summary>
    public static readonly Guid InfoUuid = Guid.Parse("6B1E0004-9F4C-4C2A-9B8E-3D2A1C0F5E01");

    /// <summary>Protocol version implemented by this receiver.</summary>
    public const int ProtocolVersion = 2;

    /// <summary>Fixed-size packets are [kind, a lo, a hi, b lo, b hi] plus optional [ms lo, ms hi].</summary>
    public const int FixedPacketLength = 5;
    public const int TimedPacketLength = 7;

    /// <summary>Largest non-info packet accepted. The default ATT MTU leaves 20 bytes of payload.</summary>
    public const int MaxFixedPacketLength = 20;

    /// <summary>Largest info packet accepted (kind byte + JSON).</summary>
    public const int MaxInfoPacketLength = 512;

    /// <summary>Aim angles are hundredths of a degree. Heading is wrapped to ±180°, elevation can't exceed ±180°.</summary>
    public const int MaxAimCentidegrees = 18000;
}
