using System.Text;

namespace WatchPointer.Core.Input;

public abstract record MacroStep;

/// <summary>Press keys in order, release in reverse ("Ctrl+Shift+Esc").</summary>
public sealed record ChordStep(IReadOnlyList<ushort> Keys) : MacroStep
{
    public override string ToString() => string.Join("+", Keys.Select(VirtualKeys.NameOf));

    public KeyStroke[] ToStrokes()
    {
        var strokes = new KeyStroke[Keys.Count * 2];
        for (var i = 0; i < Keys.Count; i++)
        {
            strokes[i] = new KeyStroke(Keys[i], true);
            strokes[Keys.Count * 2 - 1 - i] = new KeyStroke(Keys[i], false);
        }
        return strokes;
    }
}

public sealed record DelayStep(int Milliseconds) : MacroStep
{
    public override string ToString() => $"wait {Milliseconds}";
}

public sealed record TextStep(string Text) : MacroStep
{
    public override string ToString() => $"type:{Text}";
}

/// <summary>
/// A keyboard shortcut or macro, parsed from a human-editable string:
/// <code>
/// "Ctrl+Win+Right"                       one chord
/// "Win+Tab"                              Task View
/// "Ctrl+C, wait 100, Alt+Tab, Ctrl+V"    sequence with a pause
/// "type:Hello world"                     Unicode text (no comma allowed inside; use a separate step)
/// </code>
/// Key names: letters, digits, F1–F24, Ctrl/Shift/Alt/Win, Enter, Esc, Tab, Space, arrows, PageUp/PageDown, Home/End,
/// Insert/Delete, Backspace, PrintScreen, MediaPlayPause/MediaNext/MediaPrevious/MediaStop, VolumeUp/VolumeDown/VolumeMute,
/// BrowserBack/BrowserForward, Plus, Minus, Comma, Period, Slash, Num0–Num9.
/// </summary>
public sealed record KeyMacro(IReadOnlyList<MacroStep> Steps)
{
    public const int MaxSteps = 64;
    public const int MaxDelayMs = 10_000;

    public static KeyMacro Empty { get; } = new([]);

    public override string ToString() => string.Join(", ", Steps);

    public static KeyMacro Parse(string text) =>
        TryParse(text, out var macro, out var error) ? macro : throw new FormatException(error);

    public static bool TryParse(string? text, out KeyMacro macro, out string error)
    {
        macro = Empty;
        error = "";
        if (string.IsNullOrWhiteSpace(text)) { error = "Shortcut is empty."; return false; }

        var steps = new List<MacroStep>();
        foreach (var raw in text.Split(','))
        {
            var part = raw.Trim();
            if (part.Length == 0) { error = "Empty step (two commas in a row?)."; return false; }

            if (part.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            {
                var t = part[5..];
                if (t.Length == 0) { error = "type: needs some text."; return false; }
                steps.Add(new TextStep(t));
            }
            else if (part.StartsWith("wait", StringComparison.OrdinalIgnoreCase))
            {
                var number = part[4..].Trim().TrimEnd('s', 'm', 'S', 'M').Trim();
                if (!int.TryParse(number, out var ms) || ms < 0 || ms > MaxDelayMs)
                {
                    error = $"'{part}': wait needs milliseconds between 0 and {MaxDelayMs}.";
                    return false;
                }
                steps.Add(new DelayStep(ms));
            }
            else
            {
                var keys = new List<ushort>();
                foreach (var name in part.Split('+'))
                {
                    if (!VirtualKeys.TryParse(name, out var code)) { error = $"Unknown key '{name.Trim()}' in '{part}'."; return false; }
                    if (keys.Contains(code)) { error = $"Key '{name.Trim()}' repeated in '{part}'."; return false; }
                    keys.Add(code);
                }
                // Modifiers first so "Tab+Alt" still works as Alt+Tab.
                var ordered = keys.Where(VirtualKeys.IsModifier).Concat(keys.Where(k => !VirtualKeys.IsModifier(k))).ToList();
                steps.Add(new ChordStep(ordered));
            }
            if (steps.Count > MaxSteps) { error = $"At most {MaxSteps} steps."; return false; }
        }
        macro = new KeyMacro(steps);
        return true;
    }

    /// <summary>Total keys + text length, for UI summaries.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        foreach (var s in Steps)
        {
            if (sb.Length > 0) sb.Append(" → ");
            sb.Append(s);
        }
        return sb.ToString();
    }
}
