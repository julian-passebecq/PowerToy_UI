using System.ComponentModel;
using System.Runtime.InteropServices;
using JUtility.Core.Models;

namespace JUtility.App.Services;

internal sealed class GlobalMouseSummonService : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmMiddleButtonUp = 0x0208;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const int VkControl = 0x11;
    private const ushort XButton1 = 0x0001;
    private const ushort XButton2 = 0x0002;

    private readonly LowLevelMouseProc _callback;
    private IntPtr _hook;
    private SummonMouseBinding _binding;
    private bool _suppressMiddleUp;
    private ushort _suppressXButtonUp;

    public GlobalMouseSummonService()
    {
        _callback = HookCallback;
    }

    public event EventHandler? Triggered;

    public bool IsRunning => _hook != IntPtr.Zero;

    public void Start(SummonMouseBinding binding)
    {
        if (_hook != IntPtr.Zero && _binding == binding)
        {
            return;
        }

        Stop();
        _binding = binding;

        IntPtr moduleHandle = GetModuleHandle(null);
        _hook = SetWindowsHookEx(WhMouseLl, _callback, moduleHandle, 0);
        if (_hook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the global mouse hook.");
        }
    }

    public void Stop()
    {
        _suppressMiddleUp = false;
        _suppressXButtonUp = 0;

        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = unchecked((int)(long)wParam);

            if (message == WmMiddleButtonDown && MatchesMiddleTrigger())
            {
                _suppressMiddleUp = true;
                Triggered?.Invoke(this, EventArgs.Empty);
                return (IntPtr)1;
            }

            if (message == WmMiddleButtonUp && _suppressMiddleUp)
            {
                _suppressMiddleUp = false;
                return (IntPtr)1;
            }

            if (message == WmXButtonDown && TryGetMatchingXButton(lParam, out ushort button))
            {
                _suppressXButtonUp = button;
                Triggered?.Invoke(this, EventArgs.Empty);
                return (IntPtr)1;
            }

            if (message == WmXButtonUp && _suppressXButtonUp != 0)
            {
                MouseLowLevelHookStruct data = Marshal.PtrToStructure<MouseLowLevelHookStruct>(lParam);
                ushort button = (ushort)((data.MouseData >> 16) & 0xffff);
                if (button == _suppressXButtonUp)
                {
                    _suppressXButtonUp = 0;
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool MatchesMiddleTrigger()
    {
        bool ctrlPressed = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
        return _binding switch
        {
            SummonMouseBinding.MiddleClick => !ctrlPressed,
            SummonMouseBinding.CtrlMiddleClick => ctrlPressed,
            _ => false,
        };
    }

    private bool TryGetMatchingXButton(IntPtr lParam, out ushort button)
    {
        MouseLowLevelHookStruct data = Marshal.PtrToStructure<MouseLowLevelHookStruct>(lParam);
        button = (ushort)((data.MouseData >> 16) & 0xffff);
        return (_binding == SummonMouseBinding.MouseButton4 && button == XButton1)
            || (_binding == SummonMouseBinding.MouseButton5 && button == XButton2);
    }

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseLowLevelHookStruct
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr moduleHandle, uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
