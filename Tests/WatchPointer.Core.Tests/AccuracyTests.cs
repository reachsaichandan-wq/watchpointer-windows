using WatchPointer.Core.Pointer;
using WatchPointer.Core.Simulation;
using Xunit.Abstractions;

namespace WatchPointer.Core.Tests;

/// <summary>Cursor-vs-hand accuracy with a simulated watch and Bluetooth delay (see TrackingBenchmark).</summary>
public class AccuracyTests(ITestOutputHelper output)
{
    private TrackingBenchmark.Result Measure(ResponsivenessPreset preset)
    {
        var r = TrackingBenchmark.Run(PointerPresets.Apply(new PointerSettings(), preset));
        output.WriteLine($"{preset}: {r}");
        return r;
    }

    [Fact]
    public void Default_settings_are_the_balanced_preset()
    {
        Assert.Equal(ResponsivenessPreset.Balanced, PointerPresets.Detect(new PointerSettings()));
    }

    [Fact]
    public void Prediction_removes_most_of_the_bluetooth_lag()
    {
        var smooth = Measure(ResponsivenessPreset.Smooth);
        var balanced = Measure(ResponsivenessPreset.Balanced);
        Assert.True(balanced.MeanErrorPx < smooth.MeanErrorPx / 3, $"{balanced.MeanErrorPx:F1} vs {smooth.MeanErrorPx:F1} px");
        Assert.True(balanced.LagMs <= 10, $"lag {balanced.LagMs:F0} ms");
    }

    [Theory]
    [InlineData(ResponsivenessPreset.Balanced)]
    [InlineData(ResponsivenessPreset.Fast)]
    public void Prediction_keeps_a_still_hand_steady_and_overshoot_small(ResponsivenessPreset preset)
    {
        var r = Measure(preset);
        Assert.True(r.StillSpreadPx <= 12, $"still spread {r.StillSpreadPx:F1} px");
        // Steps pattern = 1340 px flicks in 250 ms (~10 000 px/s peak): overshoot must stay a small fraction.
        Assert.True(r.OvershootPx <= 70, $"overshoot {r.OvershootPx:F1} px");
    }

    [Fact]
    public void Smooth_preset_reproduces_the_original_tuning_without_prediction()
    {
        var s = PointerPresets.Apply(new PointerSettings(), ResponsivenessPreset.Smooth);
        Assert.Equal(0.8, s.SmoothingMinCutoff);
        Assert.Equal(0.01, s.SmoothingBeta);
        Assert.Equal(0.005, s.FollowTimeConstant);
        Assert.Equal(0, s.PredictionMs);
        Assert.True(Measure(ResponsivenessPreset.Smooth).OvershootPx < 1);
    }

    [Fact]
    public void Hand_tuned_settings_are_not_reported_as_a_preset()
    {
        Assert.Null(PointerPresets.Detect(new PointerSettings { SmoothingBeta = 0.033 }));
    }
}
