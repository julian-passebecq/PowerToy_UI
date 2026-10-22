using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JUtility.App;

// V2.4 capture.screen: the whole monitor under the pointer goes straight to the clipboard (no file, no editor),
// confirmed by a short toast. The region snip (capture.region) stays the way to pick a part of the screen.
public partial class MainWindow
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    private void CaptureScreenToClipboard()
    {
        // The Ring hides just before this runs: give Windows a frame or two to repaint what was under it.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                (BitmapSource image, int width, int height) = CaptureMonitorUnderPointer();
                SetClipboardImageWithRetry(image);
                _viewModel.StatusText = $"Screen copied to the clipboard ({width}×{height})";
                ShowToast("Screen copied: Ctrl+V to paste");
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = "Screen capture failed";
                ShowToast("Screen capture failed: " + ex.Message);
            }
        };
        timer.Start();
    }

    /// <summary>Physical pixels of the monitor under the pointer, whatever its scaling (per-monitor DPI for this call only).</summary>
    private static (BitmapSource Image, int Width, int Height) CaptureMonitorUnderPointer()
    {
        IntPtr previousAwareness = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        try
        {
            if (!GetCursorPos(out NativePoint pointer)) throw new InvalidOperationException("The pointer position is unknown.");
            IntPtr monitor = MonitorFromPoint(pointer, 2 /* MONITOR_DEFAULTTONEAREST */);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("The screen could not be identified.");
            int width = info.Monitor.Right - info.Monitor.Left, height = info.Monitor.Bottom - info.Monitor.Top;

            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr bitmap = CreateCompatibleBitmap(screen, width, height);
            IntPtr old = SelectObject(memory, bitmap);
            try
            {
                if (!BitBlt(memory, 0, 0, width, height, screen, info.Monitor.Left, info.Monitor.Top, 0x00CC0020 /* SRCCOPY */ | 0x40000000 /* CAPTUREBLT */))
                    throw new InvalidOperationException("Windows refused the screen copy.");
                SelectObject(memory, old);
                BitmapSource image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return (image, width, height);
            }
            finally
            {
                SelectObject(memory, old);
                DeleteObject(bitmap);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
        finally
        {
            if (previousAwareness != IntPtr.Zero) SetThreadDpiAwarenessContext(previousAwareness);
        }
    }

    private static void SetClipboardImageWithRetry(BitmapSource image)
    {
        // Another program can hold the clipboard for a moment (clipboard managers, Office).
        for (int attempt = 1; ; attempt++)
        {
            try { Clipboard.SetImage(image); return; }
            catch (COMException) when (attempt < 5) { Thread.Sleep(60); }
        }
    }

    /// <summary>Short non-activating confirmation near the pointer, for actions started while another app is in front.</summary>
    private void ShowToast(string message)
    {
        var toast = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            Title = "Power Ops notice",
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x20, 0x24, 0x2C)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 14, 8),
                Child = new TextBlock { Text = message, Foreground = Brushes.White, FontSize = 13 },
            },
        };
        toast.SourceInitialized += (_, _) =>
        {
            IntPtr handle = new WindowInteropHelper(toast).Handle;
            // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT: never takes focus or clicks.
            SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000 | 0x00000080 | 0x00000020));
        };
        toast.Loaded += (_, _) =>
        {
            Services.WindowPlacementService.CenterOnCursor(toast);
            toast.Top += 60;
        };
        toast.Show();
        var close = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        close.Tick += (_, _) => { close.Stop(); toast.Close(); };
        close.Start();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, int flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int rop);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr gdiObject);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
}
