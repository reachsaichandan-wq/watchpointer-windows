namespace WatchPointer.Core.Bluetooth;

/// <summary>What the local Bluetooth adapter can do. Peripheral role is the one that matters: watchOS can only be a
/// BLE central, so the PC must host the GATT server and advertise.</summary>
public sealed record AdapterCapabilities
{
    public bool AdapterPresent { get; init; }
    public bool LowEnergySupported { get; init; }
    public bool PeripheralRoleSupported { get; init; }
    public bool CentralRoleSupported { get; init; }
    public bool? ExtendedAdvertisingSupported { get; init; }
    public bool RadioOn { get; init; }
    public string? AdapterName { get; init; }
    public string? AdapterAddress { get; init; }
    public int? MaxAdvertisementDataLength { get; init; }
    public string? OsVersion { get; init; }

    public bool CanHostWatchPointer => AdapterPresent && LowEnergySupported && PeripheralRoleSupported;

    /// <summary>Human explanation of why the receiver can't run, or null when it can.</summary>
    public string? Problem =>
        !AdapterPresent ? "No Bluetooth adapter was found. Plug in a Bluetooth LE adapter or enable the built-in one in Device Manager."
        : !LowEnergySupported ? "This Bluetooth adapter does not support Bluetooth Low Energy (it is Bluetooth Classic only)."
        : !PeripheralRoleSupported ? "This Bluetooth adapter/driver does not support the BLE peripheral (GATT server) role. Apple Watch apps can only act as BLE central, so the PC must be the peripheral. Use an adapter whose Windows driver reports peripheral-role support (see docs/BLUETOOTH.md)."
        : !RadioOn ? "Bluetooth is turned off. Turn it on in Settings › Bluetooth & devices."
        : null;
}

public enum TransportState
{
    Stopped,
    Starting,
    /// <summary>GATT service published and advertising; waiting for the watch.</summary>
    Advertising,
    /// <summary>A watch subscribed to the output characteristic.</summary>
    Connected,
    /// <summary>Adapter can't do it; see <see cref="AdapterCapabilities.Problem"/>.</summary>
    Unsupported,
    RadioOff,
    /// <summary>Something failed; the supervisor retries with backoff.</summary>
    Faulted,
}

public sealed record TransportStartResult(bool Success, string? Error = null, bool Permanent = false);

public readonly record struct ConnectedClient(string DeviceId, int MaxPduSize);

/// <summary>The BLE peripheral side: publishes the WatchPointer GATT service, advertises it, delivers writes.</summary>
public interface IPeripheralTransport : IAsyncDisposable
{
    Task<AdapterCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<TransportStartResult> StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();

    /// <summary>Raw bytes written to the input characteristic, with the PC arrival time (seconds, monotonic clock).</summary>
    event Action<ReadOnlyMemory<byte>, double>? PacketReceived;
    event Action<ConnectedClient>? ClientConnected;
    event Action<ConnectedClient>? ClientDisconnected;
    /// <summary>Advertising stopped or aborted unexpectedly (driver reset, radio off, another app…).</summary>
    event Action<string>? Faulted;
    event Action<bool>? RadioStateChanged;

    bool IsAdvertising { get; }
    IReadOnlyList<ConnectedClient> Clients { get; }

    /// <summary>Notifies subscribed watches on the output characteristic. Returns false if nobody received it.</summary>
    Task<bool> NotifyAsync(byte[] data, CancellationToken cancellationToken = default);
}
