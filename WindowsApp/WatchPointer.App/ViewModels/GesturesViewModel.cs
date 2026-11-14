using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WatchPointer.Core.Input;
using WatchPointer.Core.Profiles;
using WatchPointer.Core.Settings;

namespace WatchPointer.App.ViewModels;

/// <summary>One editable row: a watch input and the action it triggers in the selected profile.</summary>
public sealed partial class MappingRow : ObservableObject
{
    private readonly Action<MappingRow> _changed;
    private bool _loading = true;

    public MappingRow(WatchInput input, ActionSpec action, IEnumerable<string> profiles, Action<MappingRow> changed)
    {
        Input = input;
        _changed = changed;
        ProfileNames = [.. profiles];
        _type = action.Type;
        _keys = action.Keys ?? "";
        _profile = action.Profile ?? ProfileNames.FirstOrDefault() ?? "";
        Validate();
        _loading = false;
    }

    public WatchInput Input { get; }
    public string Label => Describe(Input).Label;
    public string Hint => Describe(Input).Hint;
    public List<string> ProfileNames { get; }
    public ActionType[] Types { get; } = Enum.GetValues<ActionType>();

    [ObservableProperty] private ActionType _type;
    [ObservableProperty] private string _keys;
    [ObservableProperty] private string _profile;
    [ObservableProperty] private string _error = "";

    public bool ShowKeys => Type == ActionType.Keys;
    public bool ShowProfile => Type == ActionType.SwitchProfile;

    public ActionSpec ToSpec() => Type switch
    {
        ActionType.Keys => ActionSpec.Shortcut(Keys),
        ActionType.SwitchProfile => new ActionSpec { Type = Type, Profile = Profile },
        _ => ActionSpec.Of(Type),
    };

    partial void OnTypeChanged(ActionType value) { OnPropertyChanged(nameof(ShowKeys)); OnPropertyChanged(nameof(ShowProfile)); Commit(); }
    partial void OnKeysChanged(string value) => Commit();
    partial void OnProfileChanged(string value) => Commit();

    private void Commit()
    {
        if (_loading) return;
        if (Validate()) _changed(this);
    }

    private bool Validate()
    {
        Error = "";
        if (Type == ActionType.Keys && !KeyMacro.TryParse(Keys, out _, out var error)) { Error = error; return false; }
        if (Type == ActionType.Drag && Input != WatchInput.PadHold) { Error = "Drag only makes sense on press-and-hold; it acts as a click here."; }
        return true;
    }

    public static (string Label, string Hint) Describe(WatchInput input) => input switch
    {
        WatchInput.PadTap => ("Tap the pad", "Two quick taps = Windows double click"),
        WatchInput.PadHold => ("Press and hold the pad", "Held 0.35 s; release ends it"),
        WatchInput.RightButton => ("Right-click button", "Bottom-left button"),
        WatchInput.SwipeLeft => ("Swipe left on the pad", "Protocol v2 watch app"),
        WatchInput.SwipeRight => ("Swipe right on the pad", "Protocol v2 watch app"),
        WatchInput.SwipeUp => ("Swipe up on the pad", "Protocol v2 watch app"),
        WatchInput.SwipeDown => ("Swipe down on the pad", "Protocol v2 watch app"),
        WatchInput.HandDoubleTap => ("Pinch twice (Double Tap)", "watchOS Double Tap; Series 9 / Ultra 2 or newer"),
        WatchInput.WristFlick => ("Wrist flick (older watch app)", "Undirected flick from earlier builds"),
        WatchInput.FlickLeft => ("Flick wrist left", "Sharp turn and back; enable on the watch"),
        WatchInput.FlickRight => ("Flick wrist right", "Sharp turn and back; enable on the watch"),
        WatchInput.Shake => ("Shake", "Quick back-and-forth; on by default"),
        WatchInput.TwistClockwise => ("Twist clockwise", "Roll past 35°, hold to repeat; enable on the watch"),
        WatchInput.TwistAnticlockwise => ("Twist anticlockwise", "Roll past 35°, hold to repeat; enable on the watch"),
        >= WatchInput.ButtonA and <= WatchInput.ButtonH => ($"Tool button {(char)('A' + (input - WatchInput.ButtonA))}", "Tools screen on the watch; label shown there"),
        _ => (input.ToString(), ""),
    };
}

public sealed partial class GesturesViewModel : PageViewModel
{
    private readonly ISettingsStore _settings;
    private bool _loading;

    public GesturesViewModel(ISettingsStore settings)
    {
        _settings = settings;
        settings.Changed += s => App.OnUi(() =>
        {
            if (!s.Profiles.Select(p => p.Name).SequenceEqual(ProfileNames)) LoadProfiles(s);
        });
        LoadProfiles(settings.Current);
    }

    public override string Title => "Gestures";
    public override string Glyph => "";

    public ObservableCollection<string> ProfileNames { get; } = [];
    public ObservableCollection<MappingRow> Rows { get; } = [];
    public CrownMode[] CrownModes { get; } = Enum.GetValues<CrownMode>();

    [ObservableProperty] private string? _profileName;
    [ObservableProperty] private CrownMode _crown;
    [ObservableProperty] private bool _movePointer;

    public string Note =>
        "Mappings apply instantly. Taps, holds, the right-click button and the Crown work with every watch app version; " +
        "swipes, remote buttons, Double Tap and wrist flick need this project's watch app (protocol v2). " +
        "Keyboard shortcuts go to the focused window; media keys work everywhere.";

    private void LoadProfiles(AppSettings s)
    {
        var keep = ProfileName;
        ProfileNames.Clear();
        foreach (var p in s.Profiles) ProfileNames.Add(p.Name);
        ProfileName = ProfileNames.Contains(keep ?? "") ? keep : s.ActiveProfile;
        LoadRows();
    }

    partial void OnProfileNameChanged(string? value) => LoadRows();

    private void LoadRows()
    {
        _loading = true;
        Rows.Clear();
        var profile = _settings.Current.Profiles.FirstOrDefault(p => p.Name == ProfileName);
        if (profile is not null)
        {
            foreach (var input in Enum.GetValues<WatchInput>())
                Rows.Add(new MappingRow(input, profile.Resolve(input), ProfileNames, Save));
            Crown = profile.Crown;
            MovePointer = profile.MovePointer;
        }
        _loading = false;
    }

    partial void OnCrownChanged(CrownMode value) => SaveProfile(p => p with { Crown = value });
    partial void OnMovePointerChanged(bool value) => SaveProfile(p => p with { MovePointer = value });

    private void Save(MappingRow row) => SaveProfile(p =>
    {
        var mappings = new Dictionary<WatchInput, ActionSpec>(p.Mappings) { [row.Input] = row.ToSpec() };
        return p with { Mappings = mappings };
    });

    private void SaveProfile(Func<ControlProfile, ControlProfile> change)
    {
        if (_loading || ProfileName is null) return;
        var name = ProfileName;
        _settings.Update(s => s with { Profiles = s.Profiles.Select(p => p.Name == name ? change(p) : p).ToList() });
    }
}
