using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WatchPointer.Platform.Input;
using static WatchPointer.Platform.Native.NativeMethods;

namespace WatchPointer.App.Services;

/// <summary>
/// The presentation laser dot: a small click-through, always-on-top, non-activating window that follows the cursor.
/// It never takes focus, so key presses still reach the slideshow.
/// </summary>
public sealed class LaserOverlay
{
    private const double SizeDip = 34;
    private readonly Window _window;
    private readonly DispatcherTimer _timer;
    private readonly Win32InputSink _cursor;

    public LaserOverlay(Win32InputSink cursor)
    {
        _cursor = cursor;
        var dot = new Ellipse
        {
            Width = SizeDip,
            Height = SizeDip,
            Fill = new RadialGradientBrush(
                [new GradientStop(Color.FromArgb(255, 255, 70, 60), 0), new GradientStop(Color.FromArgb(200, 255, 30, 30), 0.35), new GradientStop(Color.FromArgb(0, 255, 0, 0), 1)]),
            IsHitTestVisible = false,
        };
        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            ResizeMode = ResizeMode.NoResize,
            Width = SizeDip,
            Height = SizeDip,
            Content = dot,
            Title = "WatchPointer laser",
        };
        _window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST);
        };
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
        _timer.Tick += (_, _) => Follow();
    }

    public bool Visible { get; private set; }

    public void Show()
    {
        if (Visible) return;
        Visible = true;
        _window.Show();
        Follow();
        _timer.Start();
    }

    public void Hide()
    {
        if (!Visible) return;
        Visible = false;
        _timer.Stop();
        _window.Hide();
    }

    private void Follow()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == 0) return;
        var p = _cursor.CursorPosition;
        var scale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
        var size = (int)Math.Round(SizeDip * scale);
        SetWindowPos(hwnd, HWND_TOPMOST, p.X - size / 2, p.Y - size / 2, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
