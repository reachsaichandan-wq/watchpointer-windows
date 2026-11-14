using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WatchPointer.App.Services;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Settings;

namespace WatchPointer.App.ViewModels;

public sealed record DisplayChoice(string? Id, string Label);

public sealed partial class PointerViewModel : PageViewModel
{
    private readonly ISettingsStore _settings;
    private readonly ReceiverHost _host;
    private bool _loading;

    public PointerViewModel(ISettingsStore settings, ReceiverHost host)
    {
        _settings = settings;
        _host = host;
        Load(settings.Current.Pointer);
        settings.Changed += s => App.OnUi(() => Load(s.Pointer));
        host.Displays.Changed += _ => App.OnUi(LoadDisplays);
        LoadDisplays();
    }

    public override string Title => "Pointer settings";
    public override string Glyph => "";

    public PointingArea[] Areas { get; } = Enum.GetValues<PointingArea>();
    public ObservableCollection<DisplayChoice> Displays { get; } = [];

    [ObservableProperty] private bool _absolute = true;
    [ObservableProperty] private double _horizontalSpeed;
    [ObservableProperty] private double _verticalSpeed;
    [ObservableProperty] private double _maxAngle;
    [ObservableProperty] private PointingArea _area;
    [ObservableProperty] private DisplayChoice? _display;
    [ObservableProperty] private bool _reanchor;
    [ObservableProperty] private double _relativeSpeed;
    [ObservableProperty] private double _relativeAcceleration;
    [ObservableProperty] private double _deadZone;
    [ObservableProperty] private bool _invertHorizontal;
    [ObservableProperty] private bool _invertVertical;
    [ObservableProperty] private double _steady;
    [ObservableProperty] private double _quick;
    [ObservableProperty] private double _followMs;
    [ObservableProperty] private int _cursorRate;
    [ObservableProperty] private double _predictionMs;
    [ObservableProperty] private bool _adaptivePrediction;
    [ObservableProperty] private string _presetText = "";
    [ObservableProperty] private double _precisionScale;
    [ObservableProperty] private double _fastScale;
    [ObservableProperty] private double _drawingScale;
    [ObservableProperty] private bool _autoPrecision;
    [ObservableProperty] private double _scrollSpeed;
    [ObservableProperty] private bool _invertScroll;
    [ObservableProperty] private bool _smoothScroll;

    public string ReachText => $"Turn {PointerSettings.BaseReachDegrees / Math.Max(0.1, HorizontalSpeed):F1}° from the middle to reach the left/right edge";

    private void Load(PointerSettings p)
    {
        _loading = true;
        Absolute = p.Mode == PointerMode.Absolute;
        HorizontalSpeed = p.HorizontalSpeed;
        VerticalSpeed = p.VerticalSpeed;
        MaxAngle = p.MaxAngleDegrees;
        Area = p.PointingArea;
        Display = Displays.FirstOrDefault(d => d.Id == p.PointingDisplayId) ?? Displays.FirstOrDefault();
        Reanchor = p.ReanchorAtEdges;
        RelativeSpeed = p.RelativeSpeed;
        RelativeAcceleration = p.RelativeAcceleration;
        DeadZone = p.RelativeDeadZoneDegPerSec;
        InvertHorizontal = p.InvertHorizontal;
        InvertVertical = p.InvertVertical;
        Steady = p.SmoothingMinCutoff;
        Quick = p.SmoothingBeta;
        FollowMs = p.FollowTimeConstant * 1000;
        CursorRate = p.CursorRateHz;
        PredictionMs = p.PredictionMs;
        AdaptivePrediction = p.AdaptivePrediction;
        PrecisionScale = p.PrecisionScale;
        FastScale = p.FastScale;
        DrawingScale = p.DrawingScale;
        AutoPrecision = p.AutoPrecision;
        PresetText = PointerPresets.Detect(p) is { } preset ? $"Current: {preset}" : "Current: custom tuning";
        ScrollSpeed = p.ScrollSpeed;
        InvertScroll = p.InvertScroll;
        SmoothScroll = p.SmoothScroll;
        OnPropertyChanged(nameof(ReachText));
        _loading = false;
    }

    private void LoadDisplays()
    {
        var selected = Display?.Id ?? _settings.Current.Pointer.PointingDisplayId;
        Displays.Clear();
        foreach (var d in _host.Displays.Current.Displays)
            Displays.Add(new DisplayChoice(d.Id, $"{d.Name} — {d.Bounds.Width}×{d.Bounds.Height} @ {d.Scale * 100:F0}%{(d.IsPrimary ? " (primary)" : "")}"));
        _loading = true;
        Display = Displays.FirstOrDefault(d => d.Id == selected) ?? Displays.FirstOrDefault();
        _loading = false;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is nameof(ReachText) or nameof(Displays) or nameof(Areas) or nameof(PresetText)) return;
        if (e.PropertyName == nameof(HorizontalSpeed)) OnPropertyChanged(nameof(ReachText));
        _settings.Update(s => s with
        {
            Pointer = s.Pointer with
            {
                Mode = Absolute ? PointerMode.Absolute : PointerMode.Relative,
                HorizontalSpeed = HorizontalSpeed,
                VerticalSpeed = VerticalSpeed,
                MaxAngleDegrees = MaxAngle,
                PointingArea = Area,
                PointingDisplayId = Display?.Id,
                ReanchorAtEdges = Reanchor,
                RelativeSpeed = RelativeSpeed,
                RelativeAcceleration = RelativeAcceleration,
                RelativeDeadZoneDegPerSec = DeadZone,
                InvertHorizontal = InvertHorizontal,
                InvertVertical = InvertVertical,
                SmoothingMinCutoff = Steady,
                SmoothingBeta = Quick,
                FollowTimeConstant = FollowMs / 1000,
                CursorRateHz = CursorRate,
                PredictionMs = PredictionMs,
                AdaptivePrediction = AdaptivePrediction,
                PrecisionScale = PrecisionScale,
                FastScale = FastScale,
                DrawingScale = DrawingScale,
                AutoPrecision = AutoPrecision,
                ScrollSpeed = ScrollSpeed,
                InvertScroll = InvertScroll,
                SmoothScroll = SmoothScroll,
            },
        });
    }

    [RelayCommand]
    private void ApplyPreset(string name)
    {
        if (!Enum.TryParse<ResponsivenessPreset>(name, out var preset)) return;
        _settings.Update(s => s with { Pointer = PointerPresets.Apply(s.Pointer, preset) });
    }

    [RelayCommand]
    private void ResetDefaults() => _settings.Update(s => s with { Pointer = new PointerSettings { Mode = s.Pointer.Mode } });
}
