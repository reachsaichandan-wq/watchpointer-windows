using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WatchPointer.Core.Diagnostics;
using WatchPointer.Core.Protocol;
using WatchPointer.Core.Session;

namespace WatchPointer.Core.Bluetooth;

/// <summary>
/// Keeps the GATT server running and wires it to the engine:
/// <list type="bullet">
/// <item>checks adapter capabilities first and reports a clear, permanent "unsupported" state instead of retrying forever;</item>
/// <item>restarts advertising with exponential backoff (1 s → 30 s) after failures or unexpected aborts;</item>
/// <item>waits for the radio to come back on;</item>
/// <item>detects connected-but-silent watches (connection timeout) and disconnects;</item>
/// <item>sends v2 latency pings and PC state to watches that support them.</item>
/// </list>
/// All work runs on the thread pool; nothing here blocks the UI thread.
/// </summary>
public sealed class ReceiverSupervisor : IAsyncDisposable
{
    private readonly IPeripheralTransport _transport;
    private readonly PointerEngine _engine;
    private readonly IClock _clock;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private readonly object _stateGate = new();
    private TransportState _state = TransportState.Stopped;
    private int _consecutiveFailures;
    private volatile bool _wakeRequested;
    private double _lastPacket;
    private double _lastConnect;

    public ReceiverSupervisor(IPeripheralTransport transport, PointerEngine engine, IClock clock, ILogger<ReceiverSupervisor>? log = null)
    {
        _transport = transport;
        _engine = engine;
        _clock = clock;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _transport.PacketReceived += OnPacket;
        _transport.ClientConnected += OnClientConnected;
        _transport.ClientDisconnected += OnClientDisconnected;
        _transport.Faulted += reason => { _log.LogWarning("Bluetooth transport faulted: {Reason}", reason); SetState(TransportState.Faulted, reason); Wake(); };
        _transport.RadioStateChanged += on => { _log.LogInformation("Bluetooth radio {State}", on ? "on" : "off"); Wake(); };
    }

    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>A connected watch that sends nothing for this long is treated as gone.</summary>
    public TimeSpan SilentConnectionTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public bool LatencyProbeEnabled { get; set; } = true;

    /// <summary>Provides the PC state message sent to v2 watches (flags + profile name).</summary>
    public Func<(PcStateFlags Flags, string Profile)>? StateProvider { get; set; }

    public TransportState State { get { lock (_stateGate) return _state; } }
    public string? StateDetail { get; private set; }
    public AdapterCapabilities? Capabilities { get; private set; }
    public int Restarts { get; private set; }
    public long BytesReceived { get; private set; }

    public event Action<TransportState, string?>? StateChanged;

    public async Task StartAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_runTask is not null) return;
            _runCts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunAsync(_runCts.Token));
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_runTask is null) return;
            await _runCts!.CancelAsync();
            try { await _runTask; } catch (OperationCanceledException) { }
            _runTask = null;
            _runCts.Dispose();
            _runCts = null;
            await _transport.StopAsync();
            _engine.WatchDisconnected();
            SetState(TransportState.Stopped, null);
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Re-checks the adapter and restarts now (e.g. after the user plugged in a new adapter).</summary>
    public async Task RestartAsync()
    {
        await StopAsync();
        _consecutiveFailures = 0;
        await StartAsync();
    }

    /// <summary>Pushes the current PC state to v2 watches right away (pause, profile change…).</summary>
    public void PublishState() => _ = SendStateAsync();

    private void Wake() => _wakeRequested = true;

    private async Task RunAsync(CancellationToken ct)
    {
        var nextPing = 0.0;
        var nextState = 0.0;
        while (!ct.IsCancellationRequested)
        {
            var state = State;
            if (state is TransportState.Stopped or TransportState.Faulted or TransportState.RadioOff or TransportState.Starting)
            {
                if (!await TryStartAsync(ct)) continue;
            }
            else if (state == TransportState.Unsupported)
            {
                // Permanent until the user retries or the radio changes.
                if (!await WaitAsync(TimeSpan.FromSeconds(5), ct)) continue;
                if (_wakeRequested) { _wakeRequested = false; SetState(TransportState.Stopped, null); }
                continue;
            }

            var now = _clock.Now;
            if (State == TransportState.Connected)
            {
                if (now - Math.Max(_lastPacket, _lastConnect) > SilentConnectionTimeout.TotalSeconds)
                {
                    _log.LogWarning("Watch connected but silent for {Seconds:F0} s; treating it as disconnected", SilentConnectionTimeout.TotalSeconds);
                    _engine.WatchDisconnected();
                    _lastConnect = now; // don't repeat every poll
                    SetState(_transport.IsAdvertising ? TransportState.Advertising : TransportState.Faulted, "connection timed out");
                }
                else if (_engine.Snapshot().Watch is { Proto: >= 2 })
                {
                    if (LatencyProbeEnabled && now >= nextPing)
                    {
                        nextPing = now + PingInterval.TotalSeconds;
                        var seq = _engine.Probe.NextPing(_clock.Now);
                        await _transport.NotifyAsync(PacketCodec.EncodePing(seq), ct);
                    }
                    if (now >= nextState)
                    {
                        nextState = now + 5;
                        await SendStateAsync();
                    }
                }
            }
            else if (State == TransportState.Advertising && !_transport.IsAdvertising)
            {
                _log.LogWarning("Advertising stopped unexpectedly; restarting");
                SetState(TransportState.Faulted, "advertising stopped");
                continue;
            }

            if (_wakeRequested && State is not TransportState.Connected and not TransportState.Advertising)
                _wakeRequested = false;
            await WaitAsync(PollInterval, ct);
        }
    }

    private async Task<bool> TryStartAsync(CancellationToken ct)
    {
        _wakeRequested = false;
        if (State != TransportState.RadioOff) SetState(TransportState.Starting, null); // no flicker while polling the radio
        AdapterCapabilities caps;
        try { caps = await _transport.GetCapabilitiesAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Could not query the Bluetooth adapter");
            caps = new AdapterCapabilities();
        }
        Capabilities = caps;

        if (caps.AdapterPresent && caps.LowEnergySupported && caps.PeripheralRoleSupported && !caps.RadioOn)
        {
            if (State != TransportState.RadioOff) _log.LogWarning("Bluetooth is off; waiting for it to be turned on");
            SetState(TransportState.RadioOff, caps.Problem);
            await WaitAsync(TimeSpan.FromSeconds(3), ct);
            return false;
        }
        if (!caps.CanHostWatchPointer)
        {
            _log.LogError("Bluetooth adapter can't host WatchPointer: {Problem}", caps.Problem);
            SetState(TransportState.Unsupported, caps.Problem);
            return false;
        }

        TransportStartResult result;
        try { result = await _transport.StartAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new TransportStartResult(false, ex.Message);
        }

        if (result.Success)
        {
            _consecutiveFailures = 0;
            SetState(_transport.Clients.Count > 0 ? TransportState.Connected : TransportState.Advertising, null);
            _log.LogInformation("GATT server running; advertising WatchPointer service {Uuid}", ProtocolConstants.ServiceUuid);
            return true;
        }

        if (result.Permanent)
        {
            SetState(TransportState.Unsupported, result.Error);
            return false;
        }
        _consecutiveFailures++;
        Restarts++;
        var delay = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, InitialBackoff.Ticks << Math.Min(_consecutiveFailures - 1, 10)));
        _log.LogWarning("Starting the GATT server failed ({Error}); retry {Attempt} in {Delay:F0} s", result.Error, _consecutiveFailures, delay.TotalSeconds);
        SetState(TransportState.Faulted, result.Error);
        try { await _transport.StopAsync(); } catch (Exception ex) { _log.LogDebug(ex, "Stop after failure"); }
        await WaitAsync(delay, ct);
        return false;
    }

    /// <summary>Waits, returning early if something asked for attention. False when cancelled.</summary>
    private async Task<bool> WaitAsync(TimeSpan delay, CancellationToken ct)
    {
        var until = DateTime.UtcNow + delay;
        while (DateTime.UtcNow < until)
        {
            if (ct.IsCancellationRequested) return false;
            if (_wakeRequested) return true;
            try { await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(50, Math.Max(1, (until - DateTime.UtcNow).TotalMilliseconds))), ct); }
            catch (OperationCanceledException) { return false; }
        }
        return !ct.IsCancellationRequested;
    }

    /// <summary>When false, packets from the watch are counted but not applied (used while the simulator drives the engine).</summary>
    public bool InputEnabled { get; set; } = true;

    private void OnPacket(ReadOnlyMemory<byte> data, double arrival)
    {
        _lastPacket = arrival;
        BytesReceived += data.Length;
        if (!InputEnabled) return;
        if (State == TransportState.Advertising && _transport.Clients.Count > 0) SetState(TransportState.Connected, null);
        _engine.HandleRaw(data.Span, arrival);
    }

    private void OnClientConnected(ConnectedClient client)
    {
        _lastConnect = _clock.Now;
        _log.LogInformation("Watch subscribed ({Device}, max PDU {Pdu})", client.DeviceId, client.MaxPduSize);
        SetState(TransportState.Connected, client.DeviceId);
    }

    private void OnClientDisconnected(ConnectedClient client)
    {
        _log.LogInformation("Watch unsubscribed ({Device})", client.DeviceId);
        if (_transport.Clients.Count == 0)
        {
            _engine.WatchDisconnected();
            SetState(_transport.IsAdvertising ? TransportState.Advertising : TransportState.Faulted, null);
        }
    }

    private async Task SendStateAsync()
    {
        if (StateProvider is not { } provider || State != TransportState.Connected) return;
        if (_engine.Snapshot().Watch is not { Proto: >= 2 }) return;
        var (flags, profile) = provider();
        try { await _transport.NotifyAsync(PacketCodec.EncodeState(flags, profile)); }
        catch (Exception ex) { _log.LogDebug(ex, "State notification failed"); }
    }

    private void SetState(TransportState state, string? detail)
    {
        lock (_stateGate)
        {
            if (_state == state && StateDetail == detail) return;
            _state = state;
            StateDetail = detail;
        }
        StateChanged?.Invoke(state, detail);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycle.Dispose();
    }
}
