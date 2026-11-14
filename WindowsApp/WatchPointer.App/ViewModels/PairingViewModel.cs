using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WatchPointer.App.Services;
using WatchPointer.Core.Bluetooth;
using WatchPointer.Core.Protocol;
using WatchPointer.Core.Settings;

namespace WatchPointer.App.ViewModels;

public sealed record CapabilityRow(string Name, string Value, StatusTone Tone, string Note);

public sealed partial class PairingViewModel : PageViewModel
{
    private readonly ReceiverHost _host;
    private readonly ISettingsStore _settings;
    private bool _loading;

    public PairingViewModel(ReceiverHost host, ISettingsStore settings)
    {
        _host = host;
        _settings = settings;
        Load(settings.Current);
        settings.Changed += s => App.OnUi(() => Load(s));
    }

    public override string Title => "Device pairing";
    public override string Glyph => "";

    public ObservableCollection<CapabilityRow> Capabilities { get; } = [];

    [ObservableProperty] private string _verdict = "Checking the Bluetooth adapter…";
    [ObservableProperty] private StatusTone _verdictTone = StatusTone.Neutral;
    [ObservableProperty] private string _receiverState = "";
    [ObservableProperty] private string _watchName = "No watch connected";
    [ObservableProperty] private string _watchDetail = "";
    [ObservableProperty] private bool _watchConnected;
    [ObservableProperty] private string _trustedWatch = "None";
    [ObservableProperty] private bool _onlyTrusted;
    [ObservableProperty] private bool _requireEncryption;
    [ObservableProperty] private bool _requestLowLatency;
    [ObservableProperty] private bool _latencyProbe;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private string _connectionParameters = "";
    [ObservableProperty] private string _pcName = Environment.MachineName;

    private void Load(AppSettings s)
    {
        _loading = true;
        OnlyTrusted = s.Bluetooth.OnlyTrustedWatch;
        RequireEncryption = s.Bluetooth.RequireEncryption;
        RequestLowLatency = s.Bluetooth.RequestLowLatency;
        LatencyProbe = s.Bluetooth.LatencyProbe;
        AutoStart = s.Bluetooth.AutoStart;
        TrustedWatch = s.Bluetooth.TrustedWatchName ?? "None";
        _loading = false;
    }

    public override void Refresh()
    {
        var caps = _host.Supervisor.Capabilities;
        if (caps is not null) ShowCapabilities(caps);
        ReceiverState = $"{_host.Supervisor.State}{(_host.Supervisor.StateDetail is { Length: > 0 } d ? $" — {d}" : "")}";
        var snap = _host.Engine.Snapshot();
        WatchConnected = snap.Watch is not null;
        if (snap.Watch is { } w)
        {
            WatchName = w.Name;
            WatchDetail = $"{w.ModelName} ({w.Model}) · watchOS {w.Os} · battery {(w.Battery >= 0 ? $"{w.Battery}%" : "unknown")} · protocol v{w.Proto}" +
                          (w.Proto < 2 ? " (original WatchPointer app: no remote buttons, swipes or latency pings)" : "");
        }
        else
        {
            WatchName = _host.Supervisor.State == TransportState.Connected ? "Watch connected (waiting for its info)" : "No watch connected";
            WatchDetail = "";
        }
        ConnectionParameters = _host.Transport.LastConnectionParameters ?? "Not reported (needs Windows 11 22H2+ and a connected watch)";
    }

    private void ShowCapabilities(AdapterCapabilities c)
    {
        Capabilities.Clear();
        static StatusTone Tone(bool ok) => ok ? StatusTone.Good : StatusTone.Bad;
        Capabilities.Add(new("Adapter", c.AdapterPresent ? $"{c.AdapterName} ({c.AdapterAddress})" : "Not found", Tone(c.AdapterPresent), ""));
        Capabilities.Add(new("Bluetooth Low Energy", c.LowEnergySupported ? "Supported" : "Not supported", Tone(c.LowEnergySupported), "Required"));
        Capabilities.Add(new("Peripheral role (GATT server)", c.PeripheralRoleSupported ? "Supported" : "Not supported", Tone(c.PeripheralRoleSupported),
            "Required: watchOS apps can only be BLE central, so the PC must be the peripheral"));
        Capabilities.Add(new("Central role", c.CentralRoleSupported ? "Supported" : "Not supported", StatusTone.Neutral, "Not used"));
        Capabilities.Add(new("Extended advertising", c.ExtendedAdvertisingSupported is { } e ? (e ? "Supported" : "Not supported") : "Unknown", StatusTone.Neutral, "Not required"));
        Capabilities.Add(new("Radio", c.RadioOn ? "On" : "Off", c.RadioOn ? StatusTone.Good : StatusTone.Warning, c.RadioOn ? "" : "Turn on Bluetooth in Settings"));
        Capabilities.Add(new("Windows", c.OsVersion ?? "", StatusTone.Neutral, ""));

        (Verdict, VerdictTone) = c.Problem is { } problem
            ? (problem, c.CanHostWatchPointer ? StatusTone.Warning : StatusTone.Bad)
            : ($"This PC can host WatchPointer. It advertises as \"{PcName}\".", StatusTone.Good);
    }

    partial void OnOnlyTrustedChanged(bool value) => Save(b => b with { OnlyTrustedWatch = value });
    partial void OnRequireEncryptionChanged(bool value) => Save(b => b with { RequireEncryption = value });
    partial void OnRequestLowLatencyChanged(bool value) => Save(b => b with { RequestLowLatency = value });
    partial void OnLatencyProbeChanged(bool value) => Save(b => b with { LatencyProbe = value });
    partial void OnAutoStartChanged(bool value) => Save(b => b with { AutoStart = value });

    private void Save(Func<BluetoothSettings, BluetoothSettings> change)
    {
        if (_loading) return;
        _settings.Update(s => s with { Bluetooth = change(s.Bluetooth) });
    }

    [RelayCommand] private void TrustWatch() => _host.TrustCurrentWatch();

    [RelayCommand] private void ForgetTrusted() => _settings.Update(s => s with { Bluetooth = s.Bluetooth with { TrustedWatchName = null, OnlyTrustedWatch = false } });

    [RelayCommand]
    private async Task Restart()
    {
        Verdict = "Restarting the receiver…";
        await _host.Supervisor.RestartAsync();
    }

    public string ProtocolInfo => $"Service {ProtocolConstants.ServiceUuid:D} · protocol v{ProtocolConstants.ProtocolVersion} (compatible with the original WatchPointer watch app)";
}
