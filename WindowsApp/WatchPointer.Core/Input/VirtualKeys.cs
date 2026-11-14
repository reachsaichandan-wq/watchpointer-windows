namespace WatchPointer.Core.Input;

/// <summary>Win32 virtual-key codes by friendly name, for shortcut strings like "Ctrl+Win+Right".</summary>
public static class VirtualKeys
{
    public const ushort Shift = 0x10, Control = 0x11, Alt = 0x12, LeftWin = 0x5B;

    private static readonly Dictionary<string, ushort> ByName = Build();
    private static readonly Dictionary<ushort, string> NameByCode = ByName
        .GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);

    private static Dictionary<string, ushort> Build()
    {
        var d = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            // Modifiers (canonical names first: NameOf uses the first name registered for a code)
            ["Ctrl"] = Control, ["Control"] = Control,
            ["Shift"] = Shift,
            ["Alt"] = Alt, ["Menu"] = Alt,
            ["Win"] = LeftWin, ["Windows"] = LeftWin, ["Meta"] = LeftWin,
            // Editing / navigation
            ["Backspace"] = 0x08, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Return"] = 0x0D, ["Pause"] = 0x13,
            ["CapsLock"] = 0x14, ["Esc"] = 0x1B, ["Escape"] = 0x1B, ["Space"] = 0x20,
            ["PageUp"] = 0x21, ["PgUp"] = 0x21, ["PageDown"] = 0x22, ["PgDn"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["PrintScreen"] = 0x2C, ["PrtSc"] = 0x2C, ["Insert"] = 0x2D, ["Ins"] = 0x2D, ["Delete"] = 0x2E, ["Del"] = 0x2E,
            ["Apps"] = 0x5D, ["ContextMenu"] = 0x5D,
            // Media / volume (handled by Windows itself, independent of the focused app)
            ["VolumeMute"] = 0xAD, ["Mute"] = 0xAD, ["VolumeDown"] = 0xAE, ["VolumeUp"] = 0xAF,
            ["MediaNext"] = 0xB0, ["NextTrack"] = 0xB0, ["MediaPrevious"] = 0xB1, ["MediaPrev"] = 0xB1, ["PreviousTrack"] = 0xB1,
            ["MediaStop"] = 0xB2, ["MediaPlayPause"] = 0xB3, ["PlayPause"] = 0xB3,
            ["BrowserBack"] = 0xA6, ["BrowserForward"] = 0xA7,
            // Punctuation (US layout VK_OEM_*)
            // ("+" and "," separate keys and steps in shortcut strings, so those two keys are spelled out.)
            ["Plus"] = 0xBB, ["="] = 0xBB, ["Minus"] = 0xBD, ["-"] = 0xBD, ["Comma"] = 0xBC,
            ["Period"] = 0xBE, ["."] = 0xBE, ["Slash"] = 0xBF, ["/"] = 0xBF, ["Semicolon"] = 0xBA, ["`"] = 0xC0,
            ["["] = 0xDB, ["]"] = 0xDD, ["\\"] = 0xDC, ["'"] = 0xDE,
        };
        for (var c = 'A'; c <= 'Z'; c++) d[c.ToString()] = c;
        for (var c = '0'; c <= '9'; c++) d[c.ToString()] = c;
        for (var i = 1; i <= 24; i++) d[$"F{i}"] = (ushort)(0x70 + i - 1);
        for (var i = 0; i <= 9; i++) d[$"Num{i}"] = (ushort)(0x60 + i);
        return d;
    }

    public static bool TryParse(string name, out ushort code) => ByName.TryGetValue(name.Trim(), out code);

    public static string NameOf(ushort code) => NameByCode.TryGetValue(code, out var n) ? n : $"0x{code:X2}";

    public static bool IsModifier(ushort code) => code is Shift or Control or Alt or LeftWin or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    /// <summary>Keys that need KEYEVENTF_EXTENDEDKEY when injected with a scan code.</summary>
    public static bool IsExtended(ushort code) => code is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5
        or >= 0xA6 and <= 0xB7;
}
