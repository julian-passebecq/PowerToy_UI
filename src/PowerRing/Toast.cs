using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PowerRing;

/// <summary>Short confirmation near the pointer that never takes focus or clicks.</summary>
internal static class Toast
{
    public static void Show(string message, int milliseconds = 1800)
    {
        var window = new Window
        {
            Title = "Power Ring notice",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            Left = -32000,
            Top = -32000,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x20, 0x22, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 9, 14, 9),
                MaxWidth = 520,
                Child = new TextBlock { Text = message, Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") },
            },
        };
        window.SourceInitialized += (_, _) =>
            Native.AddExStyle(new WindowInteropHelper(window).Handle, Native.WsExNoActivate | Native.WsExToolWindow | Native.WsExTransparent);
        window.Show();
        var (work, _, _, pointer) = Native.PointerMonitor();
        double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        int w = (int)(window.ActualWidth * scale), h = (int)(window.ActualHeight * scale);
        int x = Math.Clamp(pointer.X - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        int y = Math.Clamp(pointer.Y + (int)(40 * scale), work.Top, Math.Max(work.Top, work.Bottom - h));
        Native.SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), x, y, 0, 0, 0x0001 | 0x0010);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
        timer.Start();
    }
}
