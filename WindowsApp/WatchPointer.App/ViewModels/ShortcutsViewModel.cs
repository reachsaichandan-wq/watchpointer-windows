using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WatchPointer.App.Services;
using WatchPointer.Core.Input;
using WatchPointer.Core.Settings;

namespace WatchPointer.App.ViewModels;

public sealed record ReferenceRow(string Example, string Meaning);

public sealed partial class ShortcutsViewModel : PageViewModel
{
    private readonly ISettingsStore _settings;
    private readonly ReceiverHost _host;
    private readonly Func<IReadOnlyList<string>> _conflicts;
    private bool _loading;

    public ShortcutsViewModel(ISettingsStore settings, ReceiverHost host, HotkeyConflicts conflicts)
    {
        _settings = settings;
        _host = host;
        _conflicts = conflicts.Get;
        Load(settings.Current.Hotkeys);
    }

    public override string Title => "Keyboard shortcuts";
    public override string Glyph => "";

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _togglePause = "";
    [ObservableProperty] private string _recenter = "";
    [ObservableProperty] private string _toggleLaser = "";
    [ObservableProperty] private string _nextProfile = "";
    [ObservableProperty] private string _togglePrecision = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _testMacro = "Win+Tab";
    [ObservableProperty] private string _testResult = "";

    public ReferenceRow[] Reference { get; } =
    [
        new("Ctrl+Win+Right", "Next virtual desktop"),
        new("Win+Tab", "Task View"),
        new("Alt+Tab", "Switch to the previous app"),
        new("Win+Left / Win+Right / Win+Up", "Snap / maximise the window"),
        new("F5 · Esc · B · Right · Left", "PowerPoint: start, end, black screen, next, previous"),
        new("MediaPlayPause · MediaNext · MediaPrevious", "System media keys (work in any app)"),
        new("VolumeUp · VolumeDown · VolumeMute", "System volume"),
        new("Ctrl+C, wait 100, Alt+Tab, Ctrl+V", "Macro: steps separated by commas; wait in ms"),
        new("type:Hello", "Types Unicode text"),
    ];

    public string Limitations =>
        "Windows blocks injected input to elevated (admin) windows unless WatchPointer runs as admin, " +
        "and some keys can't be injected at all (Ctrl+Alt+Del, Win+L, the secure desktop).";

    private void Load(HotkeySettings h)
    {
        _loading = true;
        Enabled = h.Enabled;
        TogglePause = h.TogglePause;
        Recenter = h.Recenter;
        ToggleLaser = h.ToggleLaser;
        NextProfile = h.NextProfile;
        TogglePrecision = h.TogglePrecision;
        _loading = false;
        Refresh();
    }

    public override void Refresh()
    {
        var conflicts = _conflicts();
        Status = !Enabled ? "Global hotkeys are off." : conflicts.Count == 0 ? "All hotkeys registered." : string.Join("\n", conflicts);
    }

    [RelayCommand]
    private void Save()
    {
        foreach (var (name, value) in new[] { ("Pause", TogglePause), ("Recenter", Recenter), ("Laser", ToggleLaser), ("Next profile", NextProfile), ("Precision", TogglePrecision) })
        {
            if (!HotkeyService.TryParse(value, out _, out _, out var error)) { Status = $"{name}: {error}"; return; }
        }
        _settings.Update(s => s with
        {
            Hotkeys = new HotkeySettings { Enabled = Enabled, TogglePause = TogglePause, Recenter = Recenter, ToggleLaser = ToggleLaser, NextProfile = NextProfile, TogglePrecision = TogglePrecision },
        });
        Refresh();
    }

    partial void OnEnabledChanged(bool value) { if (!_loading) Save(); }

    [RelayCommand]
    private async Task Test()
    {
        if (!KeyMacro.TryParse(TestMacro, out var macro, out var error)) { TestResult = error; return; }
        TestResult = "Sending in 3 s: switch to the target window now…";
        await Task.Delay(3000);
        _host.Actions.RunKeys(TestMacro);
        TestResult = $"Sent: {macro.Describe()}";
    }
}

/// <summary>Lets the Shortcuts page show which hotkeys failed to register without depending on the HWND service.</summary>
public sealed class HotkeyConflicts
{
    public Func<IReadOnlyList<string>> Source { get; set; } = () => [];
    public IReadOnlyList<string> Get() => Source();
}
