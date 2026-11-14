using System.Text.Json.Serialization;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Profiles;

namespace WatchPointer.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
public enum ThemePreference { System, Light, Dark }

public sealed record BluetoothSettings
{
    /// <summary>Start the GATT server automatically when the app starts.</summary>
    public bool AutoStart { get; init; } = true;
    /// <summary>Require an encrypted (bonded) BLE link for writes. Experimental: triggers OS-level pairing prompts,
    /// whose behaviour between watchOS and Windows has not been verified on every adapter.</summary>
    public bool RequireEncryption { get; init; }
    /// <summary>When set, info packets from other watches are flagged and their input ignored.</summary>
    public bool OnlyTrustedWatch { get; init; }
    /// <summary>Name the trusted watch reported. Not a security boundary (any device can claim a name).</summary>
    public string? TrustedWatchName { get; init; }
    /// <summary>Ask Windows for a short connection interval once the watch connects (Windows 11 API, best effort).</summary>
    public bool RequestLowLatency { get; init; } = true;
    /// <summary>Send v2 latency pings to watches that support them.</summary>
    public bool LatencyProbe { get; init; } = true;
}

public sealed record HotkeySettings
{
    public string TogglePause { get; init; } = "Ctrl+Alt+P";
    public string Recenter { get; init; } = "Ctrl+Alt+C";
    public string ToggleLaser { get; init; } = "Ctrl+Alt+L";
    public string NextProfile { get; init; } = "Ctrl+Alt+N";
    public bool Enabled { get; init; } = true;
}

public sealed record UiSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public bool MinimizeToTray { get; init; } = true;
    public bool StartMinimized { get; init; }
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public PointerSettings Pointer { get; init; } = new();
    public BluetoothSettings Bluetooth { get; init; } = new();
    public HotkeySettings Hotkeys { get; init; } = new();
    public UiSettings Ui { get; init; } = new();
    public List<ControlProfile> Profiles { get; init; } = DefaultProfiles.Create();
    public string ActiveProfile { get; init; } = DefaultProfiles.Desktop;

    /// <summary>Repairs settings loaded from an older or hand-edited file: clamps numbers, restores missing profiles.</summary>
    public AppSettings Normalize()
    {
        var profiles = (Profiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Select(p => p with { Mappings = p.Mappings ?? [] })
            .ToList();
        foreach (var d in DefaultProfiles.Create())
            if (!profiles.Any(p => string.Equals(p.Name, d.Name, StringComparison.OrdinalIgnoreCase))) profiles.Add(d);
        var active = profiles.Any(p => p.Name == ActiveProfile) ? ActiveProfile : DefaultProfiles.Desktop;
        var pointer = Pointer ?? new();
        // v1 → v2: the default responsiveness changed from the original tuning to "Balanced" (prediction on).
        // Only settings still at the old defaults are moved; anything the user tuned is kept.
        if (SchemaVersion < 2 && pointer.SmoothingMinCutoff == 0.8 && pointer.SmoothingBeta == 0.01 && pointer.FollowTimeConstant == 0.005)
            pointer = PointerPresets.Apply(pointer, ResponsivenessPreset.Balanced);
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Pointer = pointer.Sanitized(),
            Bluetooth = Bluetooth ?? new(),
            Hotkeys = Hotkeys ?? new(),
            Ui = Ui ?? new(),
            Profiles = profiles,
            ActiveProfile = active,
        };
    }
}
