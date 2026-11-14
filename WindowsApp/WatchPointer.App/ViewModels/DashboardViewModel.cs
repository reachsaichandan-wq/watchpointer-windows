using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WatchPointer.App.Services;
using WatchPointer.Core.Bluetooth;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Session;
using WatchPointer.Core.Settings;

namespace WatchPointer.App.ViewModels;

public enum StatusTone { Neutral, Good, Warning, Bad }

public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly ReceiverHost _host;
    private readonly ISettingsStore _settings;
    private bool _updatingSelectors;

    public DashboardViewModel(ReceiverHost host, ISettingsStore settings)
    {
        _host = host;
        _settings = settings;
        ProfileNames = new ObservableCollection<string>(settings.Current.Profiles.Select(p => p.Name));
        settings.Changed += s => App.OnUi(() => SyncSelectors(s));
        SyncSelectors(settings.Current);
    }

    public override string Title => "Dashboard";
    public override string Glyph => "";

    [ObservableProperty] private string _watchStatus = "Not connected";
    [ObservableProperty] private string _watchDetail = "Open WatchPointer on your Apple Watch.";
    [ObservableProperty] private StatusTone _watchTone = StatusTone.Neutral;
    [ObservableProperty] private string _bluetoothStatus = "Starting…";
    [ObservableProperty] private string _bluetoothDetail = "";
    [ObservableProperty] private StatusTone _bluetoothTone = StatusTone.Neutral;
    [ObservableProperty] private string _battery = "—";
    [ObservableProperty] private string _sampleRate = "—";
    [ObservableProperty] private string _latency = "—";
    [ObservableProperty] private string _latencyDetail = "";
    [ObservableProperty] private bool _paused;
    [ObservableProperty] private bool _laser;
    [ObservableProperty] private bool _receiverRunning;
    [ObservableProperty] private string _selectedProfile = "";
    [ObservableProperty] private bool _absoluteMode = true;
    [ObservableProperty] private double _aimX = 0.5;
    [ObservableProperty] private double _aimY = 0.5;
    [ObservableProperty] private string _aimText = "";

    public ObservableCollection<string> ProfileNames { get; }

    /// <summary>One line for the tray tooltip.</summary>
    public string TrayStatus => $"WatchPointer: {WatchStatus}{(Paused ? " (paused)" : "")}";

    public override void Refresh()
    {
        var s = _host.Engine.Snapshot();
        var state = _host.Supervisor.State;
        ReceiverRunning = state is not TransportState.Stopped;

        (BluetoothStatus, BluetoothTone, BluetoothDetail) = state switch
        {
            TransportState.Advertising => ("Advertising", StatusTone.Good, "Waiting for the watch to connect."),
            TransportState.Connected => ("Connected", StatusTone.Good, _host.Transport.LastConnectionParameters ?? "BLE link up."),
            TransportState.Starting => ("Starting…", StatusTone.Neutral, "Checking the adapter."),
            TransportState.RadioOff => ("Bluetooth off", StatusTone.Warning, "Turn Bluetooth on in Windows Settings."),
            TransportState.Unsupported => ("Not supported", StatusTone.Bad, _host.Supervisor.StateDetail ?? ""),
            TransportState.Faulted => ("Retrying", StatusTone.Warning, _host.Supervisor.StateDetail ?? ""),
            _ => ("Stopped", StatusTone.Neutral, "Receiver is off."),
        };
        if (_host.Simulator.Running) (BluetoothStatus, BluetoothTone, BluetoothDetail) = ("Simulator", StatusTone.Warning, "Virtual watch is driving the engine; real input is suspended.");

        (WatchStatus, WatchTone, WatchDetail) = s.Phase switch
        {
            PointerPhase.Live when s.WatchUntrusted => ("Untrusted watch", StatusTone.Bad, $"{s.Watch?.Name} is not the trusted watch; input ignored."),
            PointerPhase.Live => ("Pointing", StatusTone.Good, WatchName(s)),
            PointerPhase.Paused => ("Paused on watch", StatusTone.Warning, WatchName(s)),
            PointerPhase.Idle => ("Idle", StatusTone.Warning, "Connected, but no motion samples. Wake the watch or reopen the app."),
            _ when state == TransportState.Connected => ("Connected", StatusTone.Good, WatchName(s)),
            _ => ("Not connected", StatusTone.Neutral, "Open WatchPointer on your Apple Watch and tap your PC's name."),
        };
        Battery = s.Watch is { Battery: >= 0 } w ? $"{w.Battery}%{(w.Charging ? " ⚡" : "")}" : "—";
        SampleRate = s.SampleRate > 0 ? $"{s.SampleRate:F0} Hz" : "—";
        if (s.EstimatedLatency is { } est)
        {
            Latency = $"{est * 1000:F0} ms";
            LatencyDetail = $"est.: RTT/2 {s.RoundTrip.P50 * 500:F1} ms + PC {s.PipelineLatency.P50 * 1000:F1} ms";
        }
        else if (s.PipelineLatency.HasData)
        {
            Latency = $"{s.PipelineLatency.P50 * 1000:F1} ms";
            LatencyDetail = "PC side only (watch doesn't report round trips)";
        }
        else { Latency = "—"; LatencyDetail = ""; }
        Paused = s.PcPaused;
        Laser = _host.Laser;

        var layout = _host.Displays.Current;
        var area = layout.VirtualBounds;
        AimX = area.Width > 0 ? Math.Clamp((s.Cursor.X - area.Left) / (double)area.Width, 0, 1) : 0.5;
        AimY = area.Height > 0 ? Math.Clamp((s.Cursor.Y - area.Top) / (double)area.Height, 0, 1) : 0.5;
        AimText = s.Phase == PointerPhase.Waiting ? "" : $"aim {s.AimLeft:+0.0;-0.0}° left · {s.AimUp:+0.0;-0.0}° up";
        OnPropertyChanged(nameof(TrayStatus));
    }

    private static string WatchName(EngineSnapshot s) =>
        s.Watch is { } w ? $"{w.Name} · {w.ModelName} · watchOS {w.Os}{(w.Proto >= 2 ? "" : " · original app")}" : "Watch connected";

    private void SyncSelectors(AppSettings s)
    {
        _updatingSelectors = true;
        var names = s.Profiles.Select(p => p.Name).ToList();
        if (!names.SequenceEqual(ProfileNames))
        {
            ProfileNames.Clear();
            foreach (var n in names) ProfileNames.Add(n);
        }
        SelectedProfile = s.ActiveProfile;
        AbsoluteMode = s.Pointer.Mode == PointerMode.Absolute;
        _updatingSelectors = false;
    }

    partial void OnSelectedProfileChanged(string value)
    {
        if (!_updatingSelectors && !string.IsNullOrEmpty(value)) _host.SwitchProfile(value);
    }

    partial void OnAbsoluteModeChanged(bool value)
    {
        if (_updatingSelectors) return;
        _settings.Update(s => s with { Pointer = s.Pointer with { Mode = value ? PointerMode.Absolute : PointerMode.Relative } });
    }

    [RelayCommand] private void Recenter() => _host.Recenter();
    [RelayCommand] private void TogglePause() { _host.TogglePause(); Refresh(); }
    [RelayCommand] private void ToggleLaser() { _host.Laser = !_host.Laser; Refresh(); }

    [RelayCommand]
    private async Task ToggleReceiver()
    {
        if (_host.Supervisor.State == TransportState.Stopped) await _host.Supervisor.StartAsync();
        else await _host.Supervisor.StopAsync();
        Refresh();
    }
}
