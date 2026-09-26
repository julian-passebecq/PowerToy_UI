using System.Runtime.InteropServices;
using System.Windows.Interop;
using JUtility.Core.Actions;

namespace JUtility.App.Services;

// RegisterHotKey on a message-only window: no low-level keyboard hook, no polling. Windows posts
// WM_HOTKEY only for the exact registered combinations, and the press grants foreground rights.
internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private static readonly IntPtr MessageOnlyParent = new(-3);

    private readonly Dictionary<int, PlannedHotkey> _registered = [];
    private HwndSource? _source;

    public event EventHandler<PlannedHotkey>? Pressed;

    public IReadOnlyCollection<PlannedHotkey> Registered => _registered.Values;

    /// <summary>Replaces every registration. Returns one user-facing message per binding that failed.</summary>
    public IReadOnlyList<string> Apply(IReadOnlyList<PlannedHotkey> plan)
    {
        UnregisterAll();
        if (plan.Count == 0)
        {
            return [];
        }

        _source ??= CreateSource();
        var failures = new List<string>();
        foreach (PlannedHotkey hotkey in plan)
        {
            if (RegisterHotKey(_source.Handle, hotkey.Id, (uint)hotkey.Gesture.NativeModifiers, (uint)hotkey.Gesture.VirtualKey))
            {
                _registered[hotkey.Id] = hotkey;
            }
            else
            {
                failures.Add(QuickActionHotkeys.DescribeRegistrationFailure(hotkey.Gesture, Marshal.GetLastWin32Error()));
            }
        }

        return failures;
    }

    /// <summary>
    /// True when Windows would accept this combination now: already registered by this instance, or a trial
    /// registration succeeds (and is immediately released). Nothing stays registered by the probe.
    /// </summary>
    public bool IsAvailable(HotkeyGesture gesture)
    {
        if (_registered.Values.Any(x => x.Gesture == gesture)) return true;
        _source ??= CreateSource();
        const int probeId = 0xBFF0;
        if (!RegisterHotKey(_source.Handle, probeId, (uint)gesture.NativeModifiers, (uint)gesture.VirtualKey)) return false;
        UnregisterHotKey(_source.Handle, probeId);
        return true;
    }

    public void UnregisterAll()
    {
        if (_source is not null)
        {
            foreach (int id in _registered.Keys)
            {
                UnregisterHotKey(_source.Handle, id);
            }
        }

        _registered.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
    }

    private HwndSource CreateSource()
    {
        var source = new HwndSource(new HwndSourceParameters("PowerOpsHotkeys")
        {
            ParentWindow = MessageOnlyParent,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        });
        source.AddHook(WndProc);
        return source;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _registered.TryGetValue(wParam.ToInt32(), out PlannedHotkey? hotkey))
        {
            handled = true;
            Pressed?.Invoke(this, hotkey);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
