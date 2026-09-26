using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PowerRing.Core;

namespace PowerRing;

/// <summary>Runs one ring item. Called after the ring is hidden and the previous window is back in front.</summary>
internal static class ActionRunner
{
    private static readonly Dictionary<string, Guid> KnownFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["downloads"] = new("374DE290-123F-4565-9164-39C4925E467B"),
        ["desktop"] = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
        ["documents"] = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
        ["pictures"] = new("33E28130-4E1E-4676-835A-98395C3BC3BB"),
        ["videos"] = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC"),
        ["music"] = new("4BD8D571-6D19-48D3-BE97-422220080E43"),
    };

    public static void Run(RingItem item, Dispatcher dispatcher)
    {
        string action = RingActions.Of(item);
        string target = Environment.ExpandEnvironmentVariables(item.Target?.Trim() ?? "");
        try
        {
            switch (action)
            {
                case RingActions.Run:
                    var start = new ProcessStartInfo(target) { UseShellExecute = true, Arguments = Environment.ExpandEnvironmentVariables(item.Args ?? "") };
                    if (!string.IsNullOrWhiteSpace(item.WorkingDirectory)) start.WorkingDirectory = Environment.ExpandEnvironmentVariables(item.WorkingDirectory);
                    Process.Start(start);
                    break;
                case RingActions.Url:
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    break;
                case RingActions.Folder:
                    string folder = ResolveFolder(target);
                    if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Folder not found: {folder}");
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
                    break;
                case RingActions.Keys:
                    KeyCombo combo = KeyCombo.Parse(target);
                    // Let the restored window settle in the foreground before the keys arrive.
                    Delay(dispatcher, 90, () => Native.SendCombo(combo.ModifierKeys(), combo.VirtualKey));
                    break;
                case RingActions.Text:
                    SetClipboard(() => Clipboard.SetText(item.Target!));
                    Toast.Show($"Copied: {Shorten(item.Label)}");
                    break;
                case RingActions.Screenshot:
                    Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
                    break;
                case RingActions.ScreenToClipboard:
                    int seconds = item.Delay ?? 0;
                    for (int left = seconds; left > 0; left--)
                    {
                        int shown = left;
                        Delay(dispatcher, (seconds - left) * 1000 + 60, () => Toast.Show($"Screen capture in {shown}…", 700));
                    }
                    // After the countdown toasts are gone (they would be in the picture).
                    Delay(dispatcher, seconds > 0 ? seconds * 1000 + 250 : 180, () =>
                    {
                        BitmapSource image = CaptureScreenUnderPointer();
                        SetClipboard(() => Clipboard.SetImage(image));
                        Toast.Show($"Screen copied ({image.PixelWidth}×{image.PixelHeight}): Ctrl+V to paste");
                    });
                    break;
                case RingActions.PowerMode:
                    SetPowerMode(target);
                    Toast.Show(item.Label + ": power mode set");
                    break;
                case RingActions.CloseApps:
                    Toast.Show(CloseApps(target), 3500);
                    break;
                case RingActions.PowerOps:
                    ShowPowerOps(target);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Toast.Show($"{item.Label}: {ex.Message}");
        }
    }

    // Windows 11 power mode overlays (Settings > System > Power > Power mode).
    private static readonly Dictionary<string, Guid> PowerOverlays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["efficiency"] = new("961cc777-2547-4f9d-8174-7d86181b8a7a"),
        ["balanced"] = Guid.Empty,
        ["performance"] = new("ded574b5-45a0-4f42-8737-46345c09c238"),
    };

    private static void SetPowerMode(string target)
    {
        if (!PowerOverlays.TryGetValue(target, out Guid overlay)) throw new ArgumentException($"Unknown power mode {target}.");
        int result = PowerSetActiveOverlayScheme(ref overlay);
        if (result != 0) throw new InvalidOperationException($"Windows refused the power mode change (code {result}).");
    }

    /// <summary>
    /// Asks each listed program to close, exactly like clicking its X (so it can still ask to save). Never kills,
    /// never touches Power Ring, Explorer or Claude.
    /// </summary>
    private static string CloseApps(string target)
    {
        int asked = 0;
        var names = new List<string>();
        foreach (string name in RingActions.ProcessNames(target))
        {
            if (RingActions.NeverClose.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
                        {
                            asked++;
                            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
                        }
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }
        return asked == 0 ? "Night mode: none of the listed programs was open." : $"Night mode: asked {string.Join(", ", names)} to close ({asked} window{(asked > 1 ? "s" : "")}).";
    }

    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    private static extern int PowerSetActiveOverlayScheme(ref Guid overlay);

    /// <summary>Starting Power Ops again activates the running instance (its own single-instance rule).</summary>
    public static void ShowPowerOps(string target)
    {
        string? exe = !string.IsNullOrEmpty(target) ? target : RunningPowerOps();
        if (exe is null || !File.Exists(exe))
            throw new FileNotFoundException("Power Ops is not running. Set \"target\" to JUtilityPalette.exe in ring.json to start it.");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
    }

    private static string? RunningPowerOps()
    {
        foreach (Process process in Process.GetProcessesByName("JUtilityPalette"))
        {
            using (process)
            {
                try { return process.MainModule?.FileName; } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        return null;
    }

    public static string ResolveFolder(string target)
    {
        if (target.Equals("home", StringComparison.OrdinalIgnoreCase)) return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (KnownFolders.TryGetValue(target, out Guid id) && Native.SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out IntPtr path) == 0)
        {
            try { return Marshal.PtrToStringUni(path)!; } finally { Marshal.FreeCoTaskMem(path); }
        }
        return target;
    }

    private static void Delay(Dispatcher dispatcher, int ms, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try { action(); } catch (Exception ex) when (ex is not OutOfMemoryException) { Toast.Show(ex.Message); }
        };
        timer.Start();
    }

    public static void SetClipboard(Action set)
    {
        // Clipboard managers can hold the clipboard for a moment.
        for (int attempt = 1; ; attempt++)
        {
            try { set(); return; }
            catch (COMException) when (attempt < 5) { Thread.Sleep(60); }
        }
    }

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..39] + "…";

    /// <summary>The monitor under the pointer in physical pixels (the app is per-monitor DPI aware).</summary>
    private static BitmapSource CaptureScreenUnderPointer()
    {
        var (_, bounds, _, _) = Native.PointerMonitor();
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        IntPtr memory = Native.CreateCompatibleDC(screen);
        IntPtr bitmap = Native.CreateCompatibleBitmap(screen, width, height);
        IntPtr old = Native.SelectObject(memory, bitmap);
        try
        {
            if (!Native.BitBlt(memory, 0, 0, width, height, screen, bounds.Left, bounds.Top, 0x00CC0020 | 0x40000000))
                throw new InvalidOperationException("Windows refused the screen copy.");
            Native.SelectObject(memory, old);
            BitmapSource image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            Native.SelectObject(memory, old);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memory);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
