using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using WatchPointer.Core.Input;
using WatchPointer.Core.Settings;
using static WatchPointer.Platform.Native.NativeMethods;

namespace WatchPointer.App.Services;

/// <summary>System-wide hotkeys (RegisterHotKey) on a message-only window.</summary>
public sealed class HotkeyService : IDisposable
{
    private readonly ILogger _log;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 1;

    public HotkeyService(ILogger<HotkeyService> log)
    {
        _log = log;
        _source = new HwndSource(new HwndSourceParameters("WatchPointerHotkeys") { ParentWindow = -3 /* HWND_MESSAGE */ });
        _source.AddHook(Hook);
    }

    /// <summary>Hotkeys that failed to register (usually because another app already owns them).</summary>
    public List<string> Conflicts { get; } = [];

    public void Apply(HotkeySettings settings, Action togglePause, Action recenter, Action toggleLaser, Action nextProfile)
    {
        UnregisterAll();
        Conflicts.Clear();
        if (!settings.Enabled) return;
        Register(settings.TogglePause, togglePause);
        Register(settings.Recenter, recenter);
        Register(settings.ToggleLaser, toggleLaser);
        Register(settings.NextProfile, nextProfile);
    }

    /// <summary>Validates a hotkey string: exactly one chord with at least one modifier.</summary>
    public static bool TryParse(string? text, out uint modifiers, out uint key, out string error)
    {
        modifiers = 0; key = 0;
        if (!KeyMacro.TryParse(text, out var macro, out error)) return false;
        if (macro.Steps is not [ChordStep chord]) { error = "A hotkey is a single key combination."; return false; }
        foreach (var k in chord.Keys)
        {
            switch (k)
            {
                case VirtualKeys.Control: modifiers |= MOD_CONTROL; break;
                case VirtualKeys.Alt: modifiers |= MOD_ALT; break;
                case VirtualKeys.Shift: modifiers |= MOD_SHIFT; break;
                case VirtualKeys.LeftWin: modifiers |= MOD_WIN; break;
                default:
                    if (key != 0) { error = "Only one non-modifier key is allowed."; return false; }
                    key = k;
                    break;
            }
        }
        if (key == 0) { error = "Add a key besides the modifiers."; return false; }
        if (modifiers == 0) { error = "Use at least one of Ctrl, Alt, Shift, Win."; return false; }
        return true;
    }

    private void Register(string text, Action action)
    {
        if (!TryParse(text, out var modifiers, out var key, out var error))
        {
            Conflicts.Add($"{text}: {error}");
            return;
        }
        var id = _nextId++;
        if (RegisterHotKey(_source.Handle, id, modifiers | MOD_NOREPEAT, key)) _actions[id] = action;
        else
        {
            Conflicts.Add($"{text}: already used by another app");
            _log.LogWarning("Hotkey {Hotkey} is already registered by another application", text);
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue((int)wParam, out var action))
        {
            handled = true;
            action();
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
