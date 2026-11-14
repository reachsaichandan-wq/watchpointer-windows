using WatchPointer.Core.Pointer;
using WatchPointer.Core.Protocol;

namespace WatchPointer.Core.Input;

/// <summary>A single key transition. <c>VirtualKey</c> is a Win32 VK_* code.</summary>
public readonly record struct KeyStroke(ushort VirtualKey, bool Down);

/// <summary>Where mouse and keyboard output goes. The Windows implementation uses SendInput; tests and the
/// simulator's preview mode use recording sinks.</summary>
public interface IInputSink
{
    void MoveTo(PointI position);
    void Button(MouseButton button, bool down);
    /// <summary>Wheel delta in Windows units (120 = one notch). Positive = up / right.</summary>
    void Wheel(int delta, bool horizontal);
    void Keys(ReadOnlySpan<KeyStroke> strokes);
    void Text(string text);
    PointI CursorPosition { get; }
}

/// <summary>Discards everything (benchmarks).</summary>
public sealed class NullInputSink : IInputSink
{
    public PointI CursorPosition { get; private set; }
    public void MoveTo(PointI position) => CursorPosition = position;
    public void Button(MouseButton button, bool down) { }
    public void Wheel(int delta, bool horizontal) { }
    public void Keys(ReadOnlySpan<KeyStroke> strokes) { }
    public void Text(string text) { }
}

/// <summary>Lets the engine switch between the real cursor and a preview (simulator) without rebuilding it.</summary>
public sealed class SwitchableInputSink(IInputSink initial) : IInputSink
{
    private volatile IInputSink _target = initial;
    public IInputSink Target { get => _target; set => _target = value; }
    public PointI CursorPosition => _target.CursorPosition;
    public void MoveTo(PointI position) => _target.MoveTo(position);
    public void Button(MouseButton button, bool down) => _target.Button(button, down);
    public void Wheel(int delta, bool horizontal) => _target.Wheel(delta, horizontal);
    public void Keys(ReadOnlySpan<KeyStroke> strokes) => _target.Keys(strokes);
    public void Text(string text) => _target.Text(text);
}

/// <summary>Records output; used by tests and by the simulator's preview canvas.</summary>
public sealed class RecordingInputSink : IInputSink
{
    private readonly object _gate = new();
    public List<string> Events { get; } = [];
    public PointI CursorPosition { get; private set; }
    public int Moves { get; private set; }

    public void MoveTo(PointI position) { lock (_gate) { CursorPosition = position; Moves++; } }
    public void Button(MouseButton button, bool down) { lock (_gate) Events.Add($"{button}{(down ? "Down" : "Up")}@{CursorPosition.X},{CursorPosition.Y}"); }
    public void Wheel(int delta, bool horizontal) { lock (_gate) Events.Add($"{(horizontal ? "HWheel" : "Wheel")}:{delta}"); }
    public void Keys(ReadOnlySpan<KeyStroke> strokes)
    {
        lock (_gate) foreach (var s in strokes) Events.Add($"Key{(s.Down ? "Down" : "Up")}:{VirtualKeys.NameOf(s.VirtualKey)}");
    }
    public void Text(string text) { lock (_gate) Events.Add($"Text:{text}"); }
    public void Clear() { lock (_gate) { Events.Clear(); Moves = 0; } }
    public string[] Snapshot() { lock (_gate) return [.. Events]; }
}
