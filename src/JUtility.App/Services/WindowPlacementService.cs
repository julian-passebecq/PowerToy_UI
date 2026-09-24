using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using JUtility.Core.Services;

namespace JUtility.App.Services;

internal static class WindowPlacementService
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public static void MoveNearCursor(Window window, int gap = 14)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetCursorPos(out Point cursor) || !GetWindowRect(handle, out Rect windowRect))
        {
            return;
        }

        IntPtr monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        MonitorInfo monitorInfo = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        NativeRect work = monitorInfo.WorkArea;
        WindowBounds target = WindowPlacementMath.PlaceNearCursor(
            cursor.X,
            cursor.Y,
            windowRect.Right - windowRect.Left,
            windowRect.Bottom - windowRect.Top,
            work.Left,
            work.Top,
            work.Right,
            work.Bottom,
            gap);

        SetWindowPos(
            handle,
            IntPtr.Zero,
            target.Left,
            target.Top,
            target.Width,
            target.Height,
            SwpNoZOrder | SwpNoActivate);
    }

    public static void EnsureVisible(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out Rect windowRect))
        {
            return;
        }

        IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        MonitorInfo monitorInfo = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        NativeRect work = monitorInfo.WorkArea;
        WindowBounds target = WindowPlacementMath.ClampToWorkArea(
            windowRect.Left,
            windowRect.Top,
            windowRect.Right - windowRect.Left,
            windowRect.Bottom - windowRect.Top,
            work.Left,
            work.Top,
            work.Right,
            work.Bottom);

        SetWindowPos(
            handle,
            IntPtr.Zero,
            target.Left,
            target.Top,
            target.Width,
            target.Height,
            SwpNoZOrder | SwpNoActivate);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
