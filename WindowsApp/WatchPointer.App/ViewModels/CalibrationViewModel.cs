using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WatchPointer.App.Services;
using WatchPointer.Core.Calibration;
using WatchPointer.Core.Session;
using WatchPointer.Core.Settings;

namespace WatchPointer.App.ViewModels;

/// <summary>
/// Guided calibration with a countdown (so you can hold the watch still without touching the PC): point at the
/// middle, the right edge, then the top edge; each is averaged over one second. Shaky captures are rejected.
/// </summary>
public sealed partial class CalibrationViewModel : PageViewModel
{
    private const double CaptureSeconds = 1.0, CountdownSeconds = 3.0, MaxSpread = 1.5;
    private readonly ReceiverHost _host;
    private readonly ISettingsStore _settings;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(20) };
    private readonly List<(double Left, double Up)> _samples = [];
    private (double Left, double Up) _center, _right, _top;
    private DateTime _phaseStart;
    private bool _capturing;
    private CalibrationCalculator.Result? _result;

    public CalibrationViewModel(ReceiverHost host, ISettingsStore settings)
    {
        _host = host;
        _settings = settings;
        _timer.Tick += (_, _) => OnTimer();
    }

    public override string Title => "Calibration";
    public override string Glyph => "";

    /// <summary>0 intro, 1 center, 2 right edge, 3 top edge, 4 result.</summary>
    [ObservableProperty] private int _step;
    [ObservableProperty] private string _instruction = "Calibration finds the sensitivity that fits how far you like to turn your wrist.";
    [ObservableProperty] private string _countdown = "";
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private bool _canApply;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _liveAim = "";
    [ObservableProperty] private double _targetX = 0.5;
    [ObservableProperty] private double _targetY = 0.5;

    public override void Refresh()
    {
        var snap = _host.Engine.Snapshot();
        LiveAim = snap.Phase == PointerPhase.Live
            ? $"Live aim: {snap.AimLeft:+0.0;-0.0}° left, {snap.AimUp:+0.0;-0.0}° up ({snap.SampleRate:F0} Hz)"
            : "Watch not pointing. Connect it (or start the simulator on the Diagnostics page).";
    }

    [RelayCommand]
    private void Start()
    {
        CanApply = false;
        Begin(1);
    }

    [RelayCommand]
    private void RecenterNow() => _host.Recenter();

    [RelayCommand]
    private void Apply()
    {
        if (_result is null) return;
        var r = _result;
        _settings.Update(s => s with { Pointer = s.Pointer with { HorizontalSpeed = Math.Round(r.HorizontalSpeed, 2), VerticalSpeed = Math.Round(r.VerticalSpeed, 2) } });
        _host.Recenter();
        Instruction = "Applied. The watch's current aim is now the middle of the screen.";
        CanApply = false;
    }

    [RelayCommand]
    private void Cancel()
    {
        _timer.Stop();
        Busy = false;
        Step = 0;
        Countdown = "";
        Instruction = "Calibration cancelled.";
    }

    private void Begin(int step)
    {
        Step = step;
        _samples.Clear();
        _capturing = false;
        _phaseStart = DateTime.UtcNow;
        Busy = true;
        (TargetX, TargetY, Instruction) = step switch
        {
            1 => (0.5, 0.5, "Point the watch's top edge at the MIDDLE of the screen and hold still."),
            2 => (1.0, 0.5, "Now point at the RIGHT EDGE of the screen (same height) and hold still."),
            _ => (0.5, 0.0, "Now point at the TOP EDGE of the screen (middle) and hold still."),
        };
        _timer.Start();
    }

    private void OnTimer()
    {
        var elapsed = (DateTime.UtcNow - _phaseStart).TotalSeconds;
        if (_host.Engine.Snapshot().Phase != PointerPhase.Live)
        {
            Countdown = "Waiting for the watch…";
            _phaseStart = DateTime.UtcNow;
            return;
        }
        if (!_capturing)
        {
            var left = CountdownSeconds - elapsed;
            Countdown = left > 0 ? $"{Math.Ceiling(left):0}" : "Hold…";
            if (left <= 0) { _capturing = true; _phaseStart = DateTime.UtcNow; }
            return;
        }
        _samples.Add(_host.Engine.RawAim);
        if (elapsed < CaptureSeconds) return;

        var spread = CalibrationCalculator.Spread(_samples);
        if (spread > MaxSpread)
        {
            Instruction = $"Too shaky ({spread:F1}° of movement). Rest your elbow and try again.";
            Begin(Step);
            return;
        }
        var average = CalibrationCalculator.Average(_samples);
        switch (Step)
        {
            case 1: _center = average; Begin(2); break;
            case 2: _right = average; Begin(3); break;
            default:
                _top = average;
                _timer.Stop();
                Busy = false;
                Countdown = "";
                Finish();
                break;
        }
    }

    private void Finish()
    {
        var area = _host.Displays.Current.PointingArea(_settings.Current.Pointer.PointingArea, _settings.Current.Pointer.PointingDisplayId);
        _result = CalibrationCalculator.Solve(_center, _right, _top, area);
        Step = 4;
        if (_result is null)
        {
            ResultText = "The measured turns were too small (or too large) to calibrate. Make bigger, deliberate movements to the edges.";
            CanApply = false;
            return;
        }
        ResultText = $"Edge reach: {_result.HorizontalReach:F1}° horizontally, {_result.VerticalReach:F1}° vertically.\n" +
                     $"Horizontal speed {_result.HorizontalSpeed:F2} (now {_settings.Current.Pointer.HorizontalSpeed:F2}), " +
                     $"vertical speed {_result.VerticalSpeed:F2} (now {_settings.Current.Pointer.VerticalSpeed:F2}).";
        Instruction = "Point back at the middle, then press Apply.";
        CanApply = true;
    }
}
