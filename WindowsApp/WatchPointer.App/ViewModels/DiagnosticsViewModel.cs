using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WatchPointer.App.Services;
using WatchPointer.Core.Diagnostics;
using WatchPointer.Core.Simulation;

namespace WatchPointer.App.ViewModels;

public sealed record MetricRow(string Name, string Value, string Meaning);

public sealed partial class DiagnosticsViewModel : PageViewModel
{
    private readonly ReceiverHost _host;
    private readonly MemoryLogProvider _logs;
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private DateTime _lastCpuSample = DateTime.UtcNow;

    public DiagnosticsViewModel(ReceiverHost host, MemoryLogProvider logs)
    {
        _host = host;
        _logs = logs;
        foreach (var line in logs.Lines) LogLines.Add(line.ToString());
        logs.LineAdded += line => App.OnUi(() =>
        {
            LogLines.Add(line.ToString());
            while (LogLines.Count > MemoryLogProvider.Capacity) LogLines.RemoveAt(0);
        });
        _lastCpu = _process.TotalProcessorTime;
    }

    public override string Title => "Diagnostics";
    public override string Glyph => "";

    public ObservableCollection<MetricRow> Metrics { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];
    public SimulationPattern[] Patterns { get; } = Enum.GetValues<SimulationPattern>().Where(p => p != SimulationPattern.Manual).ToArray();

    [ObservableProperty] private SimulationPattern _pattern = SimulationPattern.Circle;
    [ObservableProperty] private bool _simulateLive;
    [ObservableProperty] private bool _simulating;
    [ObservableProperty] private double _previewX = 0.5;
    [ObservableProperty] private double _previewY = 0.5;

    public override void Refresh()
    {
        var s = _host.Engine.Snapshot();
        var loop = _host.Loop.WakeLateness;
        static string Ms(RollingWindow.Summary w) => w.HasData ? $"p50 {w.P50 * 1000:F1} ms · p95 {w.P95 * 1000:F1} ms" : "—";
        static string Us(RollingWindow.Summary w) => w.HasData ? $"mean {w.Mean * 1e6:F1} µs · p95 {w.P95 * 1e6:F1} µs" : "—";

        var now = DateTime.UtcNow;
        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        var cpuPercent = (cpu - _lastCpu).TotalSeconds / Math.Max(0.001, (now - _lastCpuSample).TotalSeconds) / Environment.ProcessorCount * 100;
        _lastCpu = cpu;
        _lastCpuSample = now;

        var rows = new List<MetricRow>
        {
            new("Receiver", $"{_host.Supervisor.State}", _host.Supervisor.StateDetail ?? ""),
            new("Motion samples", s.SampleRate > 0 ? $"{s.SampleRate:F0} /s" : "—", "Aim packets per second from the watch (target ≈ 100)"),
            new("Cursor loop", $"{s.TickRate:F0} Hz{(_host.Loop.Active ? "" : " (idle)")}", $"Timer lateness {Ms(loop)}; high-resolution timer: {_host.Loop.UsingHighResolutionTimer}"),
            new("Packets", $"{s.Packets:N0} total · {s.InvalidPackets} invalid · {s.ReorderedSamples} reordered",
                s.InvalidByReason.Count == 0 ? "" : string.Join(", ", s.InvalidByReason.Select(kv => $"{kv.Key} {kv.Value}"))),
            new("Arrival interval", Ms(s.ArrivalInterval), "Time between samples arriving (BLE delivers in bursts)"),
            new("Link delay above best", Ms(s.LinkExcessDelay), "Each sample's delay beyond the quickest one seen (queueing / jitter)"),
            new("BLE round trip", s.RoundTrip.HasData ? Ms(s.RoundTrip) : "—", s.RoundTrip.HasData ? $"{_host.Engine.Probe.Received} pongs, {_host.Engine.Probe.Lost} lost" : "Needs a protocol v2 watch app"),
            new("PC pipeline", Ms(s.PipelineLatency), "Sample arrival → cursor moved"),
            new("Estimated latency", s.EstimatedLatency is { } e ? $"{e * 1000:F1} ms" : "—", "RTT/2 + PC pipeline (an estimate: assumes a symmetric link)"),
            new("Engine cost per tick", Us(s.TickCost), "CPU time inside one cursor update"),
            new("Process CPU", $"{cpuPercent:F2} %", $"Working set {_process.WorkingSet64 / 1024 / 1024} MiB"),
            new("SendInput failures", $"{_host.RealInput.Failures}", "Blocked by UIPI (elevated window in front)"),
            new("Restarts", $"{_host.Supervisor.Restarts}", $"Bytes received {_host.Supervisor.BytesReceived:N0}"),
            new("Connection", _host.Transport.LastConnectionParameters ?? "—", "Reported by Windows after connect"),
            new("Clicks sent", $"{s.Clicks}", ""),
        };
        Metrics.Clear();
        foreach (var r in rows) Metrics.Add(r);

        Simulating = _host.Simulator.Running;
        if (Simulating)
        {
            var area = _host.Displays.Current.VirtualBounds;
            var c = s.Cursor;
            PreviewX = Math.Clamp((c.X - area.Left) / (double)Math.Max(1, area.Width), 0, 1);
            PreviewY = Math.Clamp((c.Y - area.Top) / (double)Math.Max(1, area.Height), 0, 1);
        }
    }

    [RelayCommand]
    private void ToggleSimulation()
    {
        if (_host.Simulator.Running) _host.Simulator.Stop();
        else _host.Simulator.Start(Pattern, SimulateLive);
        Refresh();
    }

    [RelayCommand] private void ResetStatistics() => _host.Engine.ResetStatistics();

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(FileLogProvider.DefaultDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{FileLogProvider.DefaultDirectory}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"WatchPointer for Windows {AppInfo.Version} — {Environment.OSVersion}");
        if (_host.Supervisor.Capabilities is { } c)
            sb.AppendLine($"Adapter {c.AdapterName}: LE={c.LowEnergySupported} peripheral={c.PeripheralRoleSupported} radio={c.RadioOn}");
        foreach (var m in Metrics) sb.AppendLine($"{m.Name}: {m.Value} ({m.Meaning})");
        sb.AppendLine();
        foreach (var l in LogLines.TakeLast(60)) sb.AppendLine(l);
        System.Windows.Clipboard.SetText(sb.ToString());
    }
}

public sealed class AboutViewModel : PageViewModel
{
    public override string Title => "About";
    public override string Glyph => "";
    public string Version => $"Version {AppInfo.InformationalVersion} · protocol v{Core.Protocol.ProtocolConstants.ProtocolVersion}";
    public string Copyright => $"{AppInfo.Copyright} Author: {AppInfo.Author}.";
    public string Source => "Source code, releases and checksums: github.com/reachsaichandan-wq/watchpointer-windows";
    public string Credits =>
        "A Windows receiver and companion watch app inspired by WatchPointer by Umar Afzal (github.com/umarz317/WatchPointer, MIT License). " +
        "The Bluetooth protocol, motion model (gravity for elevation, integrated gyro for heading), laser-pointer projection, 1€ filter settings " +
        "and touch-down click timing follow that project, so its original watch app works with this receiver.";
    public string License => "MIT License. No telemetry, no accounts, no network: the watch talks to this PC directly over Bluetooth LE.";
    public string Security =>
        "Any nearby device that speaks this protocol can control the pointer while the receiver runs. Use 'Only accept the trusted watch' on the " +
        "Device pairing page (a convenience filter, not authentication) and stop the receiver when you don't need it.";
}
