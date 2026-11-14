namespace WatchPointer.Core.Pointer;

public enum ResponsivenessPreset
{
    /// <summary>The original WatchPointer tuning: steadiest, most lag.</summary>
    Smooth,
    Balanced,
    /// <summary>Mouse-like: least lag, with prediction compensating Bluetooth delay.</summary>
    Fast,
}

/// <summary>One-click tunings of smoothing, glide and prediction. Values chosen with <c>watchpointer-cli track</c>
/// (simulated hand + Bluetooth delay): see docs/ACCURACY.md for the measurements.</summary>
public static class PointerPresets
{
    public static PointerSettings Apply(PointerSettings s, ResponsivenessPreset preset) => preset switch
    {
        ResponsivenessPreset.Smooth => s with { SmoothingMinCutoff = 0.8, SmoothingBeta = 0.01, FollowTimeConstant = 0.005, PredictionMs = 0, AdaptivePrediction = false },
        ResponsivenessPreset.Balanced => s with { SmoothingMinCutoff = 1.0, SmoothingBeta = 0.02, FollowTimeConstant = 0.003, PredictionMs = 20, AdaptivePrediction = true },
        _ => s with { SmoothingMinCutoff = 1.0, SmoothingBeta = 0.04, FollowTimeConstant = 0.002, PredictionMs = 35, AdaptivePrediction = true },
    };

    /// <summary>The preset these settings match, or null if they were tuned by hand.</summary>
    public static ResponsivenessPreset? Detect(PointerSettings s)
    {
        foreach (var p in Enum.GetValues<ResponsivenessPreset>())
        {
            var reference = Apply(s, p);
            if (reference.SmoothingMinCutoff == s.SmoothingMinCutoff && reference.SmoothingBeta == s.SmoothingBeta
                && reference.FollowTimeConstant == s.FollowTimeConstant && reference.PredictionMs == s.PredictionMs
                && reference.AdaptivePrediction == s.AdaptivePrediction)
                return p;
        }
        return null;
    }
}
