using Microsoft.Extensions.Logging;
using WatchPointer.Core.Bluetooth;
using WatchPointer.Core.Diagnostics;
using WatchPointer.Core.Input;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Profiles;
using WatchPointer.Core.Protocol;
using WatchPointer.Core.Session;
using WatchPointer.Core.Settings;
using WatchPointer.Platform.Bluetooth;
using WatchPointer.Platform.Displays;
using WatchPointer.Platform.Input;
using WatchPointer.Platform.Timing;

namespace WatchPointer.App.Services;

/// <summary>
/// Composition of the receiver: Bluetooth transport + supervisor, engine, cursor loop, action runner, simulator.
/// Owns application-level state (active profile, laser) and applies settings changes live. UI-independent.
/// </summary>
public sealed class ReceiverHost : IAsyncDisposable
{
    private readonly ISettingsStore _settings;
    private readonly ILogger _log;
    private bool _laser;

    public ReceiverHost(ISettingsStore settings, ILoggerFactory loggers)
    {
        _settings = settings;
        _log = loggers.CreateLogger<ReceiverHost>();
        Clock = StopwatchClock.Instance;
        Displays = new Win32DisplayProvider(loggers.CreateLogger<Win32DisplayProvider>());
        RealInput = new Win32InputSink(loggers.CreateLogger<Win32InputSink>());
        Preview = new RecordingInputSink();
        Input = new SwitchableInputSink(RealInput);
        Engine = new PointerEngine(Input, Displays, Clock, loggers.CreateLogger<PointerEngine>());
        Actions = new ActionRunner(Input, loggers.CreateLogger<ActionRunner>());
        Engine.Actions = Actions;
        Transport = new GattPeripheralTransport(Clock, loggers.CreateLogger<GattPeripheralTransport>())
        {
            PcInfo = new PcInfo { Name = Environment.MachineName, App = AppInfo.Version },
        };
        Supervisor = new ReceiverSupervisor(Transport, Engine, Clock, loggers.CreateLogger<ReceiverSupervisor>())
        {
            StateProvider = () =>
            {
                var s = Engine.Snapshot();
                var flags = (s.PcPaused ? PcStateFlags.Paused : PcStateFlags.None)
                          | (Laser ? PcStateFlags.Laser : PcStateFlags.None)
                          | (s.SpeedMode != SpeedMode.Normal ? PcStateFlags.SpeedMode : PcStateFlags.None)
                          | (s.DragLocked ? PcStateFlags.DragLocked : PcStateFlags.None);
                return (flags, ActiveProfile.Name);
            },
        };
        Transport.ProfileStateProvider = () => PacketCodec.EncodeProfileState(new ProfileState
        {
            Profile = ActiveProfile.Name,
            Mode = Engine.SpeedMode.ToString(),
            DragLock = Engine.Snapshot().DragLocked,
            Buttons = ActiveProfile.ButtonLabels(),
        });
        SystemActions = new SystemActions(loggers.CreateLogger<SystemActions>());
        Loop = new CursorLoop(Engine.Tick, 240, loggers.CreateLogger<CursorLoop>());
        Simulator = new SimulationService(this);

        Actions.AppActionRequested += OnAppAction;
        Engine.Notice += n =>
        {
            if (n.Notice is EngineNotice.PcPaused or EngineNotice.PcResumed or EngineNotice.SpeedModeChanged or EngineNotice.DragLockChanged)
                Supervisor.PublishState();
        };
        _settings.Changed += Apply;
        Apply(_settings.Current);
    }

    public IClock Clock { get; }
    public Win32DisplayProvider Displays { get; }
    public Win32InputSink RealInput { get; }
    public RecordingInputSink Preview { get; }
    public SwitchableInputSink Input { get; }
    public PointerEngine Engine { get; }
    public ActionRunner Actions { get; }
    public GattPeripheralTransport Transport { get; }
    public ReceiverSupervisor Supervisor { get; }
    public CursorLoop Loop { get; }
    public SimulationService Simulator { get; }
    public SystemActions SystemActions { get; }

    public ControlProfile ActiveProfile { get; private set; } = DefaultProfiles.CreateDesktop();

    public bool Laser
    {
        get => _laser;
        set
        {
            if (_laser == value) return;
            _laser = value;
            LaserChanged?.Invoke(value);
            Supervisor.PublishState();
        }
    }

    public event Action<bool>? LaserChanged;
    public event Action<ControlProfile>? ProfileChanged;

    public async Task StartAsync()
    {
        Loop.Start();
        if (_settings.Current.Bluetooth.AutoStart) await Supervisor.StartAsync();
    }

    private void Apply(AppSettings s)
    {
        Engine.Settings = s.Pointer;
        Loop.RateHz = s.Pointer.CursorRateHz;
        Supervisor.LatencyProbeEnabled = s.Bluetooth.LatencyProbe;
        Transport.RequestLowLatency = s.Bluetooth.RequestLowLatency;
        Transport.RequireEncryption = s.Bluetooth.RequireEncryption; // takes effect on the next (re)start
        Engine.AcceptWatch = s.Bluetooth is { OnlyTrustedWatch: true, TrustedWatchName: { Length: > 0 } trusted }
            ? info => string.Equals(info.Name, trusted, StringComparison.Ordinal)
            : null;

        var profile = s.Profiles.FirstOrDefault(p => p.Name == s.ActiveProfile) ?? s.Profiles[0];
        var changed = profile.Name != ActiveProfile.Name;
        ActiveProfile = profile;
        Engine.Profile = profile;
        if (changed)
        {
            _log.LogInformation("Profile: {Profile}", profile.Name);
            Laser = profile.LaserPointer;
            ProfileChanged?.Invoke(profile);
            Supervisor.PublishState();
        }
    }

    public void SwitchProfile(string name)
    {
        if (_settings.Current.Profiles.All(p => p.Name != name)) return;
        _settings.Update(s => s with { ActiveProfile = name });
    }

    public void NextProfile()
    {
        var profiles = _settings.Current.Profiles;
        var index = profiles.FindIndex(p => p.Name == ActiveProfile.Name);
        SwitchProfile(profiles[(index + 1) % profiles.Count].Name);
    }

    public void TogglePause() => Engine.PcPaused = !Engine.PcPaused;

    public void TogglePrecision() =>
        Engine.SpeedMode = Engine.SpeedMode == SpeedMode.Precision ? SpeedMode.Normal : SpeedMode.Precision;

    /// <summary>Moves the pointing area to the next monitor and centres the current aim on it.</summary>
    public void NextDisplay()
    {
        var displays = Displays.Current.Displays;
        if (displays.Count < 2) return;
        var p = _settings.Current.Pointer;
        var currentId = p.PointingArea == PointingArea.SpecificDisplay ? p.PointingDisplayId : Displays.Current.Primary.Id;
        var index = displays.ToList().FindIndex(d => d.Id == currentId);
        var next = displays[(index + 1) % displays.Count];
        _settings.Update(s => s with { Pointer = s.Pointer with { PointingArea = PointingArea.SpecificDisplay, PointingDisplayId = next.Id } });
        Engine.Recenter();
        _log.LogInformation("Pointing at display {Display}", next.Name);
    }

    public void Recenter() => Engine.Recenter();

    public void TrustCurrentWatch()
    {
        if (Engine.Snapshot().Watch is not { } watch) return;
        _settings.Update(s => s with { Bluetooth = s.Bluetooth with { TrustedWatchName = watch.Name } });
    }

    private void OnAppAction(AppAction action, string? argument)
    {
        switch (action)
        {
            case AppAction.ToggleLaser: Laser = !Laser; break;
            case AppAction.NextProfile: NextProfile(); break;
            case AppAction.SwitchProfile when argument is not null: SwitchProfile(argument); break;
            case AppAction.NextDisplay: NextDisplay(); break;
            case AppAction.LockPc: SystemActions.LockPc(); break;
            case AppAction.ShowKeyboard: SystemActions.ShowKeyboard(); break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Simulator.Stop();
        await Supervisor.DisposeAsync();
        Loop.Dispose();
        await Actions.DisposeAsync();
        await Transport.DisposeAsync();
        Displays.Dispose();
    }
}

public static class AppInfo
{
    private static readonly System.Reflection.Assembly Assembly = typeof(AppInfo).Assembly;

    public static string Version =>
        Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";

    /// <summary>Version plus the git commit the build came from (the SDK appends "+&lt;sha&gt;" in CI builds).</summary>
    public static string InformationalVersion =>
        Attribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion is { } v
            ? (v.IndexOf('+') is var i and > 0 && v.Length > i + 8 ? v[..(i + 8)] : v)
            : Version;

    public static string Copyright => Attribute<System.Reflection.AssemblyCopyrightAttribute>()?.Copyright ?? "";
    public static string Author => Attribute<System.Reflection.AssemblyCompanyAttribute>()?.Company ?? "";

    private static T? Attribute<T>() where T : System.Attribute =>
        (T?)System.Attribute.GetCustomAttribute(Assembly, typeof(T));
}
