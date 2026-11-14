using WatchPointer.Core.Bluetooth;
using WatchPointer.Core.Diagnostics;
using WatchPointer.Core.Input;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Protocol;
using WatchPointer.Core.Session;

namespace WatchPointer.Core.Tests;

/// <summary>Scriptable transport for reconnection tests.</summary>
internal sealed class FakeTransport : IPeripheralTransport
{
    private readonly List<ConnectedClient> _clients = [];
    public AdapterCapabilities Capabilities { get; set; } = new() { AdapterPresent = true, LowEnergySupported = true, PeripheralRoleSupported = true, RadioOn = true };
    public Queue<TransportStartResult> StartResults { get; } = new();
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }
    public List<byte[]> Notifications { get; } = [];
    public bool IsAdvertising { get; set; }
    public IReadOnlyList<ConnectedClient> Clients => _clients.ToList();

    public event Action<ReadOnlyMemory<byte>, double>? PacketReceived;
    public event Action<ConnectedClient>? ClientConnected;
    public event Action<ConnectedClient>? ClientDisconnected;
    public event Action<string>? Faulted;
    public event Action<bool>? RadioStateChanged;

    public Task<AdapterCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Capabilities);

    public Task<TransportStartResult> StartAsync(CancellationToken cancellationToken = default)
    {
        StartCalls++;
        var r = StartResults.Count > 0 ? StartResults.Dequeue() : new TransportStartResult(true);
        IsAdvertising = r.Success;
        return Task.FromResult(r);
    }

    public Task StopAsync() { StopCalls++; IsAdvertising = false; return Task.CompletedTask; }

    public Task<bool> NotifyAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        lock (Notifications) Notifications.Add(data);
        return Task.FromResult(_clients.Count > 0);
    }

    public void Connect(string id = "watch") { _clients.Add(new ConnectedClient(id, 185)); ClientConnected?.Invoke(_clients[^1]); }
    public void Disconnect() { var c = _clients[0]; _clients.Clear(); ClientDisconnected?.Invoke(c); }
    public void Write(byte[] data, double at) => PacketReceived?.Invoke(data, at);
    public void Fault(string why) { IsAdvertising = false; Faulted?.Invoke(why); }
    public void Radio(bool on) { Capabilities = Capabilities with { RadioOn = on }; RadioStateChanged?.Invoke(on); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class ReceiverSupervisorTests
{
    private static (ReceiverSupervisor Supervisor, FakeTransport Transport, PointerEngine Engine) Create(Action<FakeTransport>? setup = null, TimeSpan? silent = null)
    {
        var transport = new FakeTransport();
        setup?.Invoke(transport);
        var engine = new PointerEngine(new RecordingInputSink(), new FixedDisplays(DisplayLayout.Single(1920, 1080)), StopwatchClock.Instance);
        var supervisor = new ReceiverSupervisor(transport, engine, StopwatchClock.Instance)
        {
            InitialBackoff = TimeSpan.FromMilliseconds(20),
            MaxBackoff = TimeSpan.FromMilliseconds(80),
            PollInterval = TimeSpan.FromMilliseconds(10),
            PingInterval = TimeSpan.FromMilliseconds(50),
            SilentConnectionTimeout = silent ?? TimeSpan.FromSeconds(30),
        };
        return (supervisor, transport, engine);
    }

    private static async Task Until(Func<bool> condition, int timeoutMs = 3000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > end) Assert.Fail("condition not met in time");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task Starts_advertising_when_the_adapter_supports_it()
    {
        var (s, t, _) = Create();
        await s.StartAsync();
        await Until(() => s.State == TransportState.Advertising);
        Assert.Equal(1, t.StartCalls);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Unsupported_adapter_is_reported_and_not_retried()
    {
        var (s, t, _) = Create(t => t.Capabilities = t.Capabilities with { PeripheralRoleSupported = false });
        await s.StartAsync();
        await Until(() => s.State == TransportState.Unsupported);
        await Task.Delay(150);
        Assert.Equal(0, t.StartCalls);
        Assert.Contains("peripheral", s.StateDetail, StringComparison.OrdinalIgnoreCase);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task No_adapter_is_reported()
    {
        var (s, _, _) = Create(t => t.Capabilities = new AdapterCapabilities { AdapterPresent = false });
        await s.StartAsync();
        await Until(() => s.State == TransportState.Unsupported);
        Assert.Contains("No Bluetooth adapter", s.StateDetail);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Failed_start_retries_with_backoff_then_succeeds()
    {
        var (s, t, _) = Create(t =>
        {
            t.StartResults.Enqueue(new TransportStartResult(false, "busy"));
            t.StartResults.Enqueue(new TransportStartResult(false, "busy"));
        });
        await s.StartAsync();
        await Until(() => s.State == TransportState.Advertising);
        Assert.Equal(3, t.StartCalls);
        Assert.Equal(2, s.Restarts);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Permanent_start_failure_stops_retrying()
    {
        var (s, t, _) = Create(t => t.StartResults.Enqueue(new TransportStartResult(false, "NotSupported", Permanent: true)));
        await s.StartAsync();
        await Until(() => s.State == TransportState.Unsupported);
        await Task.Delay(100);
        Assert.Equal(1, t.StartCalls);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Aborted_advertising_is_restarted()
    {
        var (s, t, _) = Create();
        await s.StartAsync();
        await Until(() => s.State == TransportState.Advertising);
        t.Fault("Aborted");
        await Until(() => t.StartCalls >= 2 && s.State == TransportState.Advertising);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Radio_off_waits_and_resumes_when_turned_on()
    {
        var (s, t, _) = Create(t => t.Capabilities = t.Capabilities with { RadioOn = false });
        await s.StartAsync();
        await Until(() => s.State == TransportState.RadioOff);
        Assert.Equal(0, t.StartCalls);
        t.Radio(true);
        await Until(() => s.State == TransportState.Advertising, 6000);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Connect_packets_disconnect_cycle()
    {
        var (s, t, engine) = Create();
        await s.StartAsync();
        await Until(() => s.State == TransportState.Advertising);
        t.Connect();
        Assert.Equal(TransportState.Connected, s.State);
        t.Write(PacketCodec.EncodeAim(0, 0, 100), StopwatchClock.Instance.Now);
        Assert.Equal(1, engine.Snapshot().Packets);
        t.Disconnect();
        Assert.Equal(TransportState.Advertising, s.State);
        Assert.Equal(PointerPhase.Waiting, engine.Snapshot().Phase);
        // Reconnect works without restarting the server.
        t.Connect();
        Assert.Equal(TransportState.Connected, s.State);
        Assert.Equal(1, t.StartCalls);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Silent_connection_times_out()
    {
        var (s, t, engine) = Create(silent: TimeSpan.FromMilliseconds(150));
        await s.StartAsync();
        await Until(() => s.State == TransportState.Advertising);
        t.Connect();
        t.Write(PacketCodec.EncodeInfo(new WatchInfo { Name = "W" }), StopwatchClock.Instance.Now);
        await Until(() => s.State == TransportState.Advertising);
        Assert.Null(engine.Snapshot().Watch);
        // A later packet marks it connected again.
        t.Write(PacketCodec.EncodeAim(0, 0, 5), StopwatchClock.Instance.Now);
        Assert.Equal(TransportState.Connected, s.State);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task V2_watch_gets_pings_and_state_but_v1_watch_does_not()
    {
        var (s, t, engine) = Create();
        s.StateProvider = () => (PcStateFlags.None, "Desktop");
        await s.StartAsync();
        await Until(() => s.State == TransportState.Advertising);
        t.Connect();
        t.Write(PacketCodec.EncodeInfo(new WatchInfo { Name = "Old", Proto = 1 }), StopwatchClock.Instance.Now);
        await Task.Delay(200);
        lock (t.Notifications) Assert.Empty(t.Notifications); // original watch app: output characteristic stays silent

        t.Write(PacketCodec.EncodeInfo(new WatchInfo { Name = "New", Proto = 2 }), StopwatchClock.Instance.Now);
        await Until(() => { lock (t.Notifications) return t.Notifications.Any(n => n[0] == (byte)NotificationType.Ping) && t.Notifications.Any(n => n[0] == (byte)NotificationType.State); });
        // Answer a ping: RTT gets measured.
        byte[] ping;
        lock (t.Notifications) ping = t.Notifications.First(n => n[0] == (byte)NotificationType.Ping);
        PacketCodec.TryDecodeNotification(ping, out _, out var seq, out _, out _);
        t.Write(PacketCodec.Encode(PacketKind.Pong, (short)seq), StopwatchClock.Instance.Now);
        Assert.True(engine.Snapshot().RoundTrip.HasData);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Stop_disconnects_the_engine()
    {
        var (s, t, engine) = Create();
        await s.StartAsync();
        await Until(() => s.State == TransportState.Advertising);
        t.Connect();
        t.Write(PacketCodec.EncodeInfo(new WatchInfo { Name = "W" }), StopwatchClock.Instance.Now);
        await s.StopAsync();
        Assert.Equal(TransportState.Stopped, s.State);
        Assert.Null(engine.Snapshot().Watch);
        await s.DisposeAsync();
    }
}
