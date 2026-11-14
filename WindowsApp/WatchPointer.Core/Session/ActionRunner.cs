using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WatchPointer.Core.Input;
using WatchPointer.Core.Profiles;

namespace WatchPointer.Core.Session;

/// <summary>Actions the engine can't perform itself because they belong to the app (overlay, profile list).</summary>
public enum AppAction { ToggleLaser, NextProfile, SwitchProfile }

/// <summary>Runs keyboard shortcuts and macros in order on a background worker so delays in a macro never block the
/// Bluetooth or cursor threads. Parsed macros are cached.</summary>
public sealed class ActionRunner : IAsyncDisposable
{
    private readonly IInputSink _input;
    private readonly ILogger _log;
    private readonly Channel<KeyMacro> _queue = Channel.CreateBounded<KeyMacro>(new BoundedChannelOptions(32)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });
    private readonly ConcurrentDictionary<string, KeyMacro?> _cache = new(StringComparer.Ordinal);
    private readonly Task _worker;
    private readonly CancellationTokenSource _stop = new();

    public ActionRunner(IInputSink input, ILogger<ActionRunner>? log = null)
    {
        _input = input;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _worker = Task.Run(RunAsync);
    }

    public event Action<AppAction, string?>? AppActionRequested;

    public long Executed { get; private set; }

    /// <summary>Queues a shortcut/macro. Returns false if the text doesn't parse.</summary>
    public bool RunKeys(string? keys)
    {
        if (string.IsNullOrWhiteSpace(keys)) return false;
        var macro = _cache.GetOrAdd(keys, k =>
        {
            if (KeyMacro.TryParse(k, out var m, out var error)) return m;
            _log.LogWarning("Ignoring shortcut {Keys}: {Error}", k, error);
            return null;
        });
        return macro is not null && _queue.Writer.TryWrite(macro);
    }

    public void RequestApp(AppAction action, string? argument = null) => AppActionRequested?.Invoke(action, argument);

    /// <summary>Runs synchronously; used by tests.</summary>
    public async Task DrainAsync()
    {
        while (_queue.Reader.Count > 0) await Task.Delay(5);
        await Task.Delay(20);
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var macro in _queue.Reader.ReadAllAsync(_stop.Token))
            {
                foreach (var step in macro.Steps)
                {
                    switch (step)
                    {
                        case ChordStep chord: _input.Keys(chord.ToStrokes()); break;
                        case DelayStep delay: await Task.Delay(delay.Milliseconds, _stop.Token); break;
                        case TextStep text: _input.Text(text.Text); break;
                    }
                }
                Executed++;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogError(ex, "Action worker failed"); }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _stop.CancelAsync();
        try { await _worker; } catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
