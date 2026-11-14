using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace WatchPointer.App.Services;

/// <summary>Notification-area icon with a small menu. The icon is drawn at runtime (no binary assets needed).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(Action show, Action togglePause, Action recenter, Action exit)
    {
        _image = CreateIcon(32);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open WatchPointer", null, (_, _) => show());
        menu.Items.Add("Pause / resume pointer", null, (_, _) => togglePause());
        menu.Items.Add("Recenter", null, (_, _) => recenter());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon { Icon = _image, Text = "WatchPointer", Visible = true, ContextMenuStrip = menu };
        _icon.DoubleClick += (_, _) => show();
    }

    public void SetStatus(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void Notify(string title, string text) => _icon.ShowBalloonTip(2000, title, text, Forms.ToolTipIcon.None);

    /// <summary>A watch-face ring with a cursor-crosshair dot.</summary>
    public static Icon CreateIcon(int size)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var s = size / 32f;
            using var body = new SolidBrush(Color.FromArgb(255, 28, 28, 32));
            using var ring = new Pen(Color.FromArgb(255, 0, 120, 212), 3 * s);
            using var dot = new SolidBrush(Color.FromArgb(255, 255, 70, 60));
            g.FillEllipse(body, 2 * s, 2 * s, 28 * s, 28 * s);
            g.DrawEllipse(ring, 3.5f * s, 3.5f * s, 25 * s, 25 * s);
            g.FillEllipse(dot, 12 * s, 12 * s, 8 * s, 8 * s);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    public static BitmapSource CreateWindowIcon()
    {
        using var icon = CreateIcon(64);
        return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
