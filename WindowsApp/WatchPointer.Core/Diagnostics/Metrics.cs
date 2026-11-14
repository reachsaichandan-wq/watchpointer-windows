using System.Diagnostics;

namespace WatchPointer.Core.Diagnostics;

/// <summary>Monotonic time in seconds. Abstracted so the engine can be tested with a fake clock.</summary>
public interface IClock
{
    double Now { get; }
}

public sealed class StopwatchClock : IClock
{
    public static StopwatchClock Instance { get; } = new();
    public double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

public sealed class ManualClock(double start = 1000) : IClock
{
    public double Now { get; set; } = start;
    public void Advance(double seconds) => Now += seconds;
}

/// <summary>Fixed-size window of recent samples with percentile queries. Not thread-safe; callers lock.</summary>
public sealed class RollingWindow(int capacity = 512)
{
    private readonly double[] _values = new double[capacity];
    private int _count, _next;

    public int Count => _count;

    public void Add(double value)
    {
        if (!double.IsFinite(value)) return;
        _values[_next] = value;
        _next = (_next + 1) % _values.Length;
        if (_count < _values.Length) _count++;
    }

    public void Clear() { _count = 0; _next = 0; }

    public Summary Summarize()
    {
        if (_count == 0) return Summary.Empty;
        var copy = new double[_count];
        Array.Copy(_values, copy, _count);
        Array.Sort(copy);
        double sum = 0;
        foreach (var v in copy) sum += v;
        return new Summary(_count, sum / _count, At(copy, 0.5), At(copy, 0.95), copy[^1]);
    }

    private static double At(double[] sorted, double q) => sorted[Math.Min(sorted.Length - 1, (int)Math.Floor(q * (sorted.Length - 1) + 0.5))];

    public readonly record struct Summary(int Count, double Mean, double P50, double P95, double Max)
    {
        public static Summary Empty => new(0, double.NaN, double.NaN, double.NaN, double.NaN);
        public bool HasData => Count > 0;
    }
}

/// <summary>Counts events per second over the last full second.</summary>
public sealed class RateCounter
{
    private double _windowStart;
    private int _inWindow;

    public double Rate { get; private set; }

    public void Hit(double now)
    {
        Roll(now);
        _inWindow++;
    }

    public void Roll(double now)
    {
        if (_windowStart == 0) _windowStart = now;
        var elapsed = now - _windowStart;
        if (elapsed >= 1)
        {
            Rate = elapsed < 2 ? _inWindow / elapsed : 0;
            _inWindow = 0;
            _windowStart = now;
        }
    }

    public void Reset() { _windowStart = 0; _inWindow = 0; Rate = 0; }
}
