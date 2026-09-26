using System.Runtime.InteropServices;

namespace PowerRing;

internal static class Native
{
    public const int WmHotkey = 0x0312;
    public const int GwlExStyle = -20;
    public const long WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExTransparent = 0x20;

    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor, Work; public int Flags; }

    [StructLayout(LayoutKind.Sequential)]
    // INPUT is 40 bytes on x64: the union is sized by MOUSEINPUT (32), KEYBDINPUT is 24, so 8 bytes of padding follow it.
    public struct Input { public int Type; public KeyboardInput Ki; private readonly long _pad; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public IntPtr Extra; }

    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(Point point, int flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int rop);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr gdiObject);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHGetKnownFolderPath(ref Guid id, int flags, IntPtr token, out IntPtr path);

    public static void AddExStyle(IntPtr window, long style) =>
        SetWindowLongPtr(window, GwlExStyle, new IntPtr(GetWindowLongPtr(window, GwlExStyle).ToInt64() | style));

    /// <summary>Monitor under the pointer: its work area in physical pixels and its scale (1.0 = 96 dpi).</summary>
    public static (Rect Work, Rect Bounds, double Scale, Point Pointer) PointerMonitor()
    {
        GetCursorPos(out Point pointer);
        IntPtr monitor = MonitorFromPoint(pointer, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        return (info.Work, info.Monitor, scale, pointer);
    }

    /// <summary>Presses a combination (modifiers down, key down/up, modifiers up) in one SendInput call.</summary>
    public static void SendCombo(IReadOnlyList<int> modifiers, int key)
    {
        var inputs = new List<Input>();
        void Add(int vk, bool up) => inputs.Add(new Input
        {
            Type = 1,
            Ki = new KeyboardInput { Vk = (ushort)vk, Flags = (up ? 2u : 0u) | (IsExtended(vk) ? 1u : 0u) },
        });
        foreach (int m in modifiers) Add(m, false);
        Add(key, false);
        Add(key, true);
        for (int i = modifiers.Count - 1; i >= 0; i--) Add(modifiers[i], true);
        SendInput((uint)inputs.Count, [.. inputs], Marshal.SizeOf<Input>());
    }

    private static bool IsExtended(int vk) => vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C;
}
