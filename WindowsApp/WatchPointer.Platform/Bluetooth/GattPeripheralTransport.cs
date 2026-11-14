using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Foundation.Metadata;
using WatchPointer.Core.Bluetooth;
using WatchPointer.Core.Diagnostics;
using WatchPointer.Core.Protocol;

namespace WatchPointer.Platform.Bluetooth;

/// <summary>
/// Windows BLE peripheral: publishes the WatchPointer GATT service with <see cref="GattServiceProvider"/> and advertises
/// it, exactly mirroring the macOS receiver's CBPeripheralManager setup (service 6B1E0001, input 0002 write/write-without-
/// response, output 0003 notify) plus the v2 read-only info characteristic 0004.
/// <para>Requires an adapter whose driver reports <c>IsPeripheralRoleSupported</c>; this is checked first and reported
/// instead of failing silently.</para>
/// </summary>
public sealed class GattPeripheralTransport : IPeripheralTransport
{
    private readonly IClock _clock;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _clientsGate = new();
    private readonly Dictionary<string, ClientState> _clients = [];
    private GattServiceProvider? _provider;
    private GattLocalCharacteristic? _input, _output, _info, _profile;
    private Radio? _radio;
    private volatile bool _advertising;
    private TaskCompletionSource<GattServiceProviderAdvertisementStatusChangedEventArgs>? _advertisingStarted;
    private bool _stopping;

    private sealed class ClientState(ConnectedClient client)
    {
        public ConnectedClient Client { get; set; } = client;
        public BluetoothLEDevice? Device { get; set; }
        public IDisposable? ParametersRequest { get; set; }
    }

    public GattPeripheralTransport(IClock clock, ILogger<GattPeripheralTransport>? log = null)
    {
        _clock = clock;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Require an encrypted (bonded) link for writes. Experimental.</summary>
    public bool RequireEncryption { get; set; }
    /// <summary>Ask for a short connection interval after connect (Windows 11 22H2+ API; best effort).</summary>
    public bool RequestLowLatency { get; set; } = true;
    /// <summary>JSON served on the info characteristic.</summary>
    public PcInfo PcInfo { get; set; } = new() { Name = Environment.MachineName, App = "1.0.0" };

    /// <summary>Supplies the profile characteristic's current value (active profile, mode, button labels).</summary>
    public Func<byte[]>? ProfileStateProvider { get; set; }

    public event Action<ReadOnlyMemory<byte>, double>? PacketReceived;
    public event Action<ConnectedClient>? ClientConnected;
    public event Action<ConnectedClient>? ClientDisconnected;
    public event Action<string>? Faulted;
    public event Action<bool>? RadioStateChanged;

    public bool IsAdvertising => _advertising;

    /// <summary>Connection parameters reported by Windows (interval in ms), when the API exists. Diagnostics only.</summary>
    public string? LastConnectionParameters { get; private set; }

    public IReadOnlyList<ConnectedClient> Clients
    {
        get { lock (_clientsGate) return _clients.Values.Select(c => c.Client).ToList(); }
    }

    public async Task<AdapterCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellationToken);
        if (adapter is null) return new AdapterCapabilities { AdapterPresent = false, OsVersion = Environment.OSVersion.VersionString };

        var radio = await adapter.GetRadioAsync().AsTask(cancellationToken);
        if (radio is not null && !ReferenceEquals(radio, _radio))
        {
            if (_radio is not null) _radio.StateChanged -= OnRadioStateChanged;
            _radio = radio;
            _radio.StateChanged += OnRadioStateChanged;
        }

        bool? extended = ApiInformation.IsPropertyPresent(typeof(BluetoothAdapter).FullName, nameof(BluetoothAdapter.IsExtendedAdvertisingSupported))
            ? adapter.IsExtendedAdvertisingSupported : null;
        int? maxAdv = ApiInformation.IsPropertyPresent(typeof(BluetoothAdapter).FullName, nameof(BluetoothAdapter.MaxAdvertisementDataLength))
            ? (int)adapter.MaxAdvertisementDataLength : null;

        return new AdapterCapabilities
        {
            AdapterPresent = true,
            LowEnergySupported = adapter.IsLowEnergySupported,
            PeripheralRoleSupported = adapter.IsPeripheralRoleSupported,
            CentralRoleSupported = adapter.IsCentralRoleSupported,
            ExtendedAdvertisingSupported = extended,
            MaxAdvertisementDataLength = maxAdv,
            RadioOn = radio?.State == RadioState.On,
            AdapterName = radio?.Name,
            AdapterAddress = FormatAddress(adapter.BluetoothAddress),
            OsVersion = Environment.OSVersion.VersionString,
        };
    }

    public async Task<TransportStartResult> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            _stopping = false;

            var created = await GattServiceProvider.CreateAsync(ProtocolConstants.ServiceUuid).AsTask(cancellationToken);
            if (created.Error != BluetoothError.Success)
            {
                var permanent = created.Error is BluetoothError.NotSupported or BluetoothError.DisabledByPolicy or BluetoothError.DisabledByUser;
                return new TransportStartResult(false, $"Creating the GATT service failed: {created.Error}", permanent);
            }
            _provider = created.ServiceProvider;

            var protection = RequireEncryption ? GattProtectionLevel.EncryptionRequired : GattProtectionLevel.Plain;
            var input = await _provider.Service.CreateCharacteristicAsync(ProtocolConstants.InputUuid, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
                WriteProtectionLevel = protection,
                UserDescription = "WatchPointer input",
            }).AsTask(cancellationToken);
            if (input.Error != BluetoothError.Success) return new TransportStartResult(false, $"Input characteristic: {input.Error}");
            _input = input.Characteristic;
            _input.WriteRequested += OnWriteRequested;

            var output = await _provider.Service.CreateCharacteristicAsync(ProtocolConstants.OutputUuid, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Notify,
                ReadProtectionLevel = GattProtectionLevel.Plain,
                UserDescription = "WatchPointer output",
            }).AsTask(cancellationToken);
            if (output.Error != BluetoothError.Success) return new TransportStartResult(false, $"Output characteristic: {output.Error}");
            _output = output.Characteristic;
            _output.SubscribedClientsChanged += OnSubscribedClientsChanged;

            var info = await _provider.Service.CreateCharacteristicAsync(ProtocolConstants.InfoUuid, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read,
                ReadProtectionLevel = GattProtectionLevel.Plain,
                StaticValue = PacketCodec.EncodePcInfo(PcInfo).AsBuffer(),
                UserDescription = "WatchPointer PC info",
            }).AsTask(cancellationToken);
            if (info.Error == BluetoothError.Success) _info = info.Characteristic;
            else _log.LogWarning("Info characteristic unavailable ({Error}); v2 watches will show a generic name", info.Error);

            var profile = await _provider.Service.CreateCharacteristicAsync(ProtocolConstants.ProfileUuid, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read,
                ReadProtectionLevel = GattProtectionLevel.Plain,
                UserDescription = "WatchPointer profile",
            }).AsTask(cancellationToken);
            if (profile.Error == BluetoothError.Success)
            {
                _profile = profile.Characteristic;
                _profile.ReadRequested += OnProfileReadRequested;
            }
            else _log.LogWarning("Profile characteristic unavailable ({Error}); watch buttons will show generic labels", profile.Error);

            _advertisingStarted = new TaskCompletionSource<GattServiceProviderAdvertisementStatusChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            _provider.AdvertisementStatusChanged += OnAdvertisementStatusChanged;
            _provider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });

            var completed = await Task.WhenAny(_advertisingStarted.Task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
            if (completed != _advertisingStarted.Task)
            {
                // Some drivers never raise the event; trust the provider's own status.
                if (_provider.AdvertisementStatus is GattServiceProviderAdvertisementStatus.Started or GattServiceProviderAdvertisementStatus.StartedWithoutAllAdvertisementData)
                {
                    _advertising = true;
                    return new TransportStartResult(true);
                }
                return new TransportStartResult(false, $"Advertising did not start (status {_provider.AdvertisementStatus})");
            }
            var status = await _advertisingStarted.Task;
            if (status.Status is GattServiceProviderAdvertisementStatus.Started or GattServiceProviderAdvertisementStatus.StartedWithoutAllAdvertisementData)
            {
                if (status.Status == GattServiceProviderAdvertisementStatus.StartedWithoutAllAdvertisementData)
                    _log.LogWarning("Advertising started without all advertisement data (the adapter dropped some fields)");
                return new TransportStartResult(true);
            }
            return new TransportStartResult(false, $"Advertising failed: {status.Status} ({status.Error})",
                Permanent: status.Error is BluetoothError.NotSupported or BluetoothError.DisabledByPolicy);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Starting the GATT server threw");
            return new TransportStartResult(false, ex.Message);
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _gate.Release(); }
    }

    private Task StopCoreAsync()
    {
        _stopping = true;
        _advertising = false;
        if (_provider is not null)
        {
            _provider.AdvertisementStatusChanged -= OnAdvertisementStatusChanged;
            try { if (_provider.AdvertisementStatus != GattServiceProviderAdvertisementStatus.Stopped) _provider.StopAdvertising(); }
            catch (Exception ex) { _log.LogDebug(ex, "StopAdvertising"); }
        }
        if (_input is not null) _input.WriteRequested -= OnWriteRequested;
        if (_output is not null) _output.SubscribedClientsChanged -= OnSubscribedClientsChanged;
        if (_profile is not null) _profile.ReadRequested -= OnProfileReadRequested;
        _provider = null;
        _input = _output = _info = _profile = null;

        List<ClientState> dropped;
        lock (_clientsGate)
        {
            dropped = [.. _clients.Values];
            _clients.Clear();
        }
        foreach (var c in dropped)
        {
            Release(c);
            ClientDisconnected?.Invoke(c.Client);
        }
        return Task.CompletedTask;
    }

    public async Task<bool> NotifyAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        var output = _output;
        if (output is null) return false;
        try
        {
            var results = await output.NotifyValueAsync(data.AsBuffer()).AsTask(cancellationToken);
            return results.Any(r => r.Status == GattCommunicationStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "Notify failed");
            return false;
        }
    }

    // ------------------------------------------------------------------ events

    private async void OnWriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        var arrival = _clock.Now; // as early as possible: closest to radio arrival
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            if (request is null) return; // access denied (e.g. encryption required but link not encrypted)
            var bytes = request.Value.ToArray();
            if (request.Option == GattWriteOption.WriteWithResponse) request.Respond(); // free the watch for its next write
            PacketReceived?.Invoke(bytes, arrival);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Write request failed");
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>Dynamic read: the current profile JSON, honouring the request offset for long (blob) reads.</summary>
    private async void OnProfileReadRequested(GattLocalCharacteristic sender, GattReadRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            if (request is null) return;
            var value = ProfileStateProvider?.Invoke() ?? [];
            var offset = (int)Math.Min(request.Offset, (uint)value.Length);
            request.RespondWithValue(value.AsSpan(offset).ToArray().AsBuffer());
        }
        catch (Exception ex) { _log.LogDebug(ex, "Profile read failed"); }
        finally { deferral.Complete(); }
    }

    private void OnSubscribedClientsChanged(GattLocalCharacteristic sender, object args)
    {
        var current = new Dictionary<string, GattSubscribedClient>();
        foreach (var c in sender.SubscribedClients) current[c.Session.DeviceId.Id] = c;

        List<ClientState> added = [], removed = [];
        lock (_clientsGate)
        {
            foreach (var (id, client) in current)
            {
                if (_clients.ContainsKey(id)) continue;
                var state = new ClientState(new ConnectedClient(id, client.Session.MaxPduSize));
                _clients[id] = state;
                added.Add(state);
            }
            foreach (var id in _clients.Keys.Where(k => !current.ContainsKey(k)).ToList())
            {
                removed.Add(_clients[id]);
                _clients.Remove(id);
            }
        }
        foreach (var r in removed) { Release(r); ClientDisconnected?.Invoke(r.Client); }
        foreach (var a in added)
        {
            ClientConnected?.Invoke(a.Client);
            if (RequestLowLatency) _ = TuneConnectionAsync(a);
        }
    }

    /// <summary>Asks for a short connection interval so samples arrive as often as possible (the macOS receiver does the
    /// same with setDesiredConnectionLatency(.low)). The watch, as central, has the final say; the outcome is logged.</summary>
    private async Task TuneConnectionAsync(ClientState state)
    {
        try
        {
            var device = await BluetoothLEDevice.FromIdAsync(state.Client.DeviceId);
            if (device is null) return;
            state.Device = device;
            const string type = "Windows.Devices.Bluetooth.BluetoothLEDevice";
            if (ApiInformation.IsMethodPresent(type, "RequestPreferredConnectionParameters"))
            {
                var request = device.RequestPreferredConnectionParameters(BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
                state.ParametersRequest = request;
                _log.LogInformation("Low-latency connection request: {Status}", request.Status);
            }
            else
            {
                _log.LogInformation("Connection-parameter API not available on this Windows version; using the watch's defaults");
            }
            if (ApiInformation.IsMethodPresent(type, "GetConnectionParameters"))
            {
                await Task.Delay(1500); // parameter update takes a few connection events
                var p = device.GetConnectionParameters();
                LastConnectionParameters = $"interval {p.ConnectionInterval * 1.25:F2} ms, latency {p.ConnectionLatency}, timeout {p.LinkTimeout * 10} ms";
                _log.LogInformation("Connection parameters: {Parameters}", LastConnectionParameters);
            }
        }
        catch (Exception ex)
        {
            _log.LogInformation(ex, "Could not tune the connection (not fatal)");
        }
    }

    private void OnAdvertisementStatusChanged(GattServiceProvider sender, GattServiceProviderAdvertisementStatusChangedEventArgs args)
    {
        _log.LogDebug("Advertisement status {Status} ({Error})", args.Status, args.Error);
        _advertising = args.Status is GattServiceProviderAdvertisementStatus.Started or GattServiceProviderAdvertisementStatus.StartedWithoutAllAdvertisementData;
        if (_advertisingStarted is { Task.IsCompleted: false } tcs && args.Status != GattServiceProviderAdvertisementStatus.Created)
        {
            tcs.TrySetResult(args);
            return;
        }
        if (!_stopping && args.Status is GattServiceProviderAdvertisementStatus.Aborted or GattServiceProviderAdvertisementStatus.Stopped)
            Faulted?.Invoke($"advertising {args.Status} ({args.Error})");
    }

    private void OnRadioStateChanged(Radio sender, object args) => RadioStateChanged?.Invoke(sender.State == RadioState.On);

    private static void Release(ClientState c)
    {
        try { c.ParametersRequest?.Dispose(); } catch { /* best effort */ }
        try { c.Device?.Dispose(); } catch { /* best effort */ }
    }

    private static string FormatAddress(ulong address) =>
        string.Join(":", Enumerable.Range(0, 6).Reverse().Select(i => ((address >> (8 * i)) & 0xFF).ToString("X2")));

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_radio is not null) _radio.StateChanged -= OnRadioStateChanged;
        _gate.Dispose();
    }
}
