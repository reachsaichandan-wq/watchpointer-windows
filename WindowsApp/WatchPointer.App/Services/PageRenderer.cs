using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WatchPointer.App.ViewModels;

namespace WatchPointer.App.Services;

/// <summary>
/// <c>WatchPointer.exe --render-pages &lt;folder&gt; [--light]</c>: renders every page off-screen to PNG and exits.
/// Used for documentation screenshots and for checking layouts in CI without touching the desktop.
/// </summary>
public static class PageRenderer
{
    public static async Task RenderAllAsync(Window window, MainViewModel main, string folder, bool light)
    {
        Directory.CreateDirectory(folder);
        App.Current.ThemeMode = light ? ThemeMode.Light : ThemeMode.Dark;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        foreach (var page in main.Pages)
        {
            main.SelectedPage = page;
            page.Refresh();
            await Dispatcher.Yield(DispatcherPriority.Background);
            await Task.Delay(400);
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            // Paint the theme background first (the window background isn't part of the content visual).
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // The live window gets its background from the system backdrop (Mica), which an off-screen render
                // doesn't include, so paint the Fluent base colour for the theme.
                var background = new SolidColorBrush(light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20));
                dc.DrawRectangle(background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var name = string.Concat(page.Title.Where(char.IsLetterOrDigit)).ToLowerInvariant();
            await using var file = File.Create(Path.Combine(folder, $"{name}{(light ? "-light" : "")}.png"));
            encoder.Save(file);
        }
    }
}
