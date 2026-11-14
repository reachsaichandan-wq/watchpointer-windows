using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WatchPointer.Core.Pointer;
using static WatchPointer.Platform.Native.NativeMethods;

namespace WatchPointer.Platform.Displays;

/// <summary>
/// Enumerates monitors with EnumDisplayMonitors. Coordinates are physical pixels because the process is
/// per-monitor-v2 DPI aware (see <see cref="DpiAwareness"/>). Refreshes on demand and every few seconds so plugging in a
/// monitor or changing the arrangement is picked up without a restart.
/// </summary>
public sealed class Win32DisplayProvider : IDisplayProvider, IDisposable
{
    private readonly ILogger _log;
    private readonly Timer _timer;
    private volatile DisplayLayout _current;
    private string _signature = "";

    public Win32DisplayProvider(ILogger<Win32DisplayProvider>? log = null)
    {
        _log = (ILogger?)log ?? NullLogger.Instance;
        _current = Enumerate();
        _signature = Signature(_current);
        _timer = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    public DisplayLayout Current => _current;

    public event Action<DisplayLayout>? Changed;

    public void Refresh()
    {
        try
        {
            var layout = Enumerate();
            var signature = Signature(layout);
            if (signature == _signature) return;
            _signature = signature;
            _current = layout;
            _log.LogInformation("Display layout changed: {Layout}", signature);
            Changed?.Invoke(layout);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not enumerate displays");
        }
    }

    private static unsafe DisplayLayout Enumerate()
    {
        var list = new List<DisplayInfo>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW { cbSize = sizeof(MONITORINFOEXW) };
            if (!GetMonitorInfo(monitor, &info)) return true;
            var device = new string(info.szDevice);
            var scale = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
            var b = info.rcMonitor;
            var w = info.rcWork;
            list.Add(new DisplayInfo(
                device,
                $"{(list.Count + 1)}: {device.TrimStart('\\', '.')}",
                new RectI(b.Left, b.Top, b.Right, b.Bottom),
                new RectI(w.Left, w.Top, w.Right, w.Bottom),
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                scale));
            return true;
        }, 0);
        return new DisplayLayout(list);
    }

    private static string Signature(DisplayLayout l) =>
        string.Join("; ", l.Displays.Select(d => $"{d.Id} {d.Bounds} @{d.Scale * 100:F0}%{(d.IsPrimary ? " primary" : "")}"));

    public void Dispose() => _timer.Dispose();
}

public static class DpiAwareness
{
    /// <summary>Makes the process per-monitor-v2 DPI aware so all coordinates are physical pixels. The WPF app does this
    /// through its manifest; the console tool calls this at startup.</summary>
    public static bool EnablePerMonitorV2() => SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
}
