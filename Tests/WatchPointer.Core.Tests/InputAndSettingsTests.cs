using System.Text.Json;
using WatchPointer.Core.Diagnostics;
using WatchPointer.Core.Input;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Profiles;
using WatchPointer.Core.Settings;

namespace WatchPointer.Core.Tests;

public class KeyMacroTests
{
    [Fact]
    public void Parses_a_chord_with_modifiers_first()
    {
        var m = KeyMacro.Parse("Tab+Alt");
        var chord = Assert.IsType<ChordStep>(Assert.Single(m.Steps));
        Assert.Equal([VirtualKeys.Alt, (ushort)0x09], chord.Keys);
        Assert.Equal("Alt+Tab", chord.ToString());
    }

    [Fact]
    public void Chord_strokes_press_in_order_and_release_in_reverse()
    {
        var strokes = ((ChordStep)KeyMacro.Parse("Ctrl+Shift+Esc").Steps[0]).ToStrokes();
        Assert.Equal(
            [new KeyStroke(0x11, true), new KeyStroke(0x10, true), new KeyStroke(0x1B, true),
             new KeyStroke(0x1B, false), new KeyStroke(0x10, false), new KeyStroke(0x11, false)],
            strokes);
    }

    [Fact]
    public void Parses_sequences_waits_and_text()
    {
        var m = KeyMacro.Parse("Ctrl+C, wait 150ms, Win+Tab, type:Hello world");
        Assert.Equal(4, m.Steps.Count);
        Assert.Equal(150, Assert.IsType<DelayStep>(m.Steps[1]).Milliseconds);
        Assert.Equal("Hello world", Assert.IsType<TextStep>(m.Steps[3]).Text);
    }

    [Theory]
    [InlineData("MediaPlayPause", 0xB3)]
    [InlineData("VolumeUp", 0xAF)]
    [InlineData("F5", 0x74)]
    [InlineData("PageDown", 0x22)]
    [InlineData("win", 0x5B)]
    [InlineData("b", 0x42)]
    public void Known_key_names(string name, int vk)
    {
        Assert.Equal((ushort)vk, ((ChordStep)KeyMacro.Parse(name).Steps[0]).Keys[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Ctrl+Ctrl")]
    [InlineData("A,,B")]
    [InlineData("wait 99999")]
    [InlineData("wait abc")]
    [InlineData("type:")]
    public void Rejects_bad_shortcuts_with_a_message(string text)
    {
        Assert.False(KeyMacro.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Default_profiles_contain_only_valid_shortcuts()
    {
        foreach (var p in DefaultProfiles.Create())
            foreach (var (input, action) in p.Mappings)
                if (action.Type == ActionType.Keys)
                    Assert.True(KeyMacro.TryParse(action.Keys, out _, out var error), $"{p.Name}/{input}: {error}");
    }
}

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wp-tests-" + Guid.NewGuid().ToString("N"));
    private string PathFor => System.IO.Path.Combine(_dir, "settings.json");

    [Fact]
    public void Missing_file_gives_defaults_with_all_profiles()
    {
        using var store = new JsonSettingsStore(PathFor);
        Assert.Equal(DefaultProfiles.Desktop, store.Current.ActiveProfile);
        Assert.Equal(4, store.Current.Profiles.Count);
    }

    [Fact]
    public void Roundtrips_through_json()
    {
        using (var store = new JsonSettingsStore(PathFor))
        {
            store.Update(s => s with
            {
                Pointer = s.Pointer with { Mode = PointerMode.Relative, HorizontalSpeed = 1.6 },
                ActiveProfile = DefaultProfiles.Media,
            });
            store.Save();
        }
        using var reloaded = new JsonSettingsStore(PathFor);
        Assert.Equal(PointerMode.Relative, reloaded.Current.Pointer.Mode);
        Assert.Equal(1.6, reloaded.Current.Pointer.HorizontalSpeed);
        Assert.Equal(DefaultProfiles.Media, reloaded.Current.ActiveProfile);
        var media = reloaded.Current.Profiles.Single(p => p.Name == DefaultProfiles.Media);
        Assert.Equal("MediaPlayPause", media.Resolve(WatchInput.PadTap).Keys);
        Assert.Equal(CrownMode.Volume, media.Crown);
    }

    [Fact]
    public void Enums_are_stored_as_names()
    {
        using (var store = new JsonSettingsStore(PathFor)) store.Save();
        var json = File.ReadAllText(PathFor);
        Assert.Contains("\"Absolute\"", json);
        Assert.Contains("\"PadTap\"", json);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_keeps_a_copy()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor, "{ this is not json");
        using var store = new JsonSettingsStore(PathFor);
        Assert.Equal(DefaultProfiles.Desktop, store.Current.ActiveProfile);
        Assert.True(File.Exists(PathFor + ".bad"));
    }

    [Fact]
    public void Normalize_clamps_values_restores_profiles_and_fixes_active()
    {
        var s = new AppSettings
        {
            Pointer = new PointerSettings { HorizontalSpeed = 999, CursorRateHz = 5 },
            Profiles = [new ControlProfile { Name = "Mine" }],
            ActiveProfile = "Gone",
        }.Normalize();
        Assert.Equal(10, s.Pointer.HorizontalSpeed);
        Assert.Equal(60, s.Pointer.CursorRateHz);
        Assert.Equal(5, s.Profiles.Count);
        Assert.Equal(DefaultProfiles.Desktop, s.ActiveProfile);
    }

    [Fact]
    public void Older_file_without_new_fields_loads()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor, """{ "pointer": { "horizontalSpeed": 1.2 } }""");
        using var store = new JsonSettingsStore(PathFor);
        Assert.Equal(1.2, store.Current.Pointer.HorizontalSpeed);
        Assert.Equal(new PointerSettings().SmoothingMinCutoff, store.Current.Pointer.SmoothingMinCutoff);
        Assert.Equal(new PointerSettings().PredictionMs, store.Current.Pointer.PredictionMs);
        Assert.True(store.Current.Bluetooth.AutoStart);
    }

    [Fact]
    public void Version_1_file_with_untouched_tuning_migrates_to_balanced_but_custom_tuning_is_kept()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor, """{ "schemaVersion": 1, "pointer": { "smoothingMinCutoff": 0.8, "smoothingBeta": 0.01, "followTimeConstant": 0.005, "predictionMs": 0 } }""");
        using (var store = new JsonSettingsStore(PathFor))
            Assert.Equal(ResponsivenessPreset.Balanced, PointerPresets.Detect(store.Current.Pointer));

        File.WriteAllText(PathFor, """{ "schemaVersion": 1, "pointer": { "smoothingMinCutoff": 0.5, "smoothingBeta": 0.01, "followTimeConstant": 0.005, "predictionMs": 0 } }""");
        using var custom = new JsonSettingsStore(PathFor);
        Assert.Equal(0.5, custom.Current.Pointer.SmoothingMinCutoff);
        Assert.Equal(0, custom.Current.Pointer.PredictionMs);
        Assert.Equal(AppSettings.CurrentSchemaVersion, custom.Current.SchemaVersion);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public class DiagnosticsTests
{
    [Fact]
    public void Rolling_window_percentiles()
    {
        var w = new RollingWindow(100);
        for (var i = 1; i <= 100; i++) w.Add(i);
        var s = w.Summarize();
        Assert.Equal(50.5, s.Mean, 6);
        Assert.InRange(s.P50, 50, 51);
        Assert.InRange(s.P95, 95, 96);
        Assert.Equal(100, s.Max);
    }

    [Fact]
    public void Rolling_window_keeps_only_recent_values()
    {
        var w = new RollingWindow(10);
        for (var i = 0; i < 100; i++) w.Add(i);
        Assert.Equal(10, w.Count);
        Assert.Equal(90, w.Summarize().Mean - 4.5, 6);
    }

    [Fact]
    public void Rate_counter_counts_per_second()
    {
        var r = new RateCounter();
        for (var t = 0.0; t < 2.0; t += 0.01) r.Hit(100 + t);
        Assert.InRange(r.Rate, 95, 105);
    }

    [Fact]
    public void Latency_probe_measures_rtt_and_expires_lost_pings()
    {
        var p = new LatencyProbe();
        var a = p.NextPing(10.0);
        Assert.Equal(0.030, p.OnPong(a, 10.030)!.Value, 6);
        var b = p.NextPing(11.0);
        Assert.Null(p.OnPong((ushort)(b + 100), 11.01)); // unknown sequence
        p.NextPing(20.0);                               // b expired by now
        Assert.Equal(1, p.Lost);
        Assert.Null(p.OnPong(b, 20.01));
        Assert.Equal(1, p.RoundTrip.Count);
    }
}
