using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using PowerRing.Core;

namespace PowerRing;

// Owns everything that lives while Power Ring runs: ring.json (+ reload on save), the global hotkey, the tray icon and
// the ring window. Idle cost: one message-only window, one FileSystemWatcher and one wait handle; no timers.
internal sealed class RingHost : IDisposable
{
    private const int HotkeyId = 0x5252;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunValue = "PowerRing";

    private readonly RingConfigStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly HwndSource _messages;
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private readonly FileSystemWatcher _watcher;
    private DispatcherTimer? _reloadDelay;
    private RingConfig _config;
    private RingWindow _ring;
    private string _hotkeyStatus = "";

    public RingHost(RingConfigStore store, Dispatcher dispatcher)
    {
        _store = store;
        _dispatcher = dispatcher;
        _store.EnsureFiles();
        string? loadError = null;
        try { _config = _store.Load(); }
        catch (RingConfigException ex) { loadError = ex.Message; _config = RingDefaults.Create(); }

        _messages = new HwndSource(new HwndSourceParameters("PowerRingMessages") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        _messages.AddHook(OnMessage);
        _ring = CreateRing(_config);

        _tray = new System.Windows.Forms.NotifyIcon { Icon = TrayIcon.Create(), Visible = true, Text = "Power Ring" };
        _tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) _dispatcher.BeginInvoke(ShowRing); };
        _tray.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
        _tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();

        _watcher = new FileSystemWatcher(_store.Directory, RingConfigStore.FileName) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        _watcher.Changed += (_, _) => ScheduleReload();
        _watcher.Created += (_, _) => ScheduleReload();
        _watcher.Renamed += (_, _) => ScheduleReload();
        _watcher.EnableRaisingEvents = true;

        RegisterHotkey();
        if (loadError is not null) Notify("ring.json has a problem, the built-in ring is used", loadError, error: true);
    }

    public void ShowRing() => _ring.Toggle();

    public void SetProfile(int index) => _ring.SetProfile(index);

    private RingWindow CreateRing(RingConfig config)
    {
        var ring = new RingWindow(config);
        ring.Invoked += (_, item) =>
        {
            if (item is null)
            {
                try { ActionRunner.ShowPowerOps(FindPowerOpsTarget(_config)); }
                catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception) { Toast.Show(ex.Message, 3500); }
            }
            else ActionRunner.Run(item, _dispatcher);
        };
        return ring;
    }

    /// <summary>The centre button uses the first "powerops" item's target, if any has one.</summary>
    private static string FindPowerOpsTarget(RingConfig config)
    {
        IEnumerable<RingItem> All(IEnumerable<RingItem> items) => items.SelectMany(x => x.Items is null ? [x] : All(x.Items).Prepend(x));
        return config.Profiles.SelectMany(p => All(p.Items))
            .FirstOrDefault(x => RingActions.Of(x) == RingActions.PowerOps && !string.IsNullOrWhiteSpace(x.Target))?.Target ?? "";
    }

    private void ScheduleReload()
    {
        // Editors write a file several times in a row: reload once, shortly after the last change.
        _dispatcher.BeginInvoke(() =>
        {
            _reloadDelay ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => { _reloadDelay!.Stop(); Reload(); }, _dispatcher);
            _reloadDelay.Stop();
            _reloadDelay.Start();
        });
    }

    public void Reload(bool announce = true)
    {
        try
        {
            RingConfig config = _store.Load();
            bool hotkeyChanged = config.Hotkey != _config.Hotkey;
            _config = config;
            _ring.Reload(config);
            if (hotkeyChanged) RegisterHotkey();
            if (announce) Toast.Show("Power Ring: ring.json reloaded");
        }
        catch (RingConfigException ex)
        {
            Notify("ring.json not applied (the previous version stays active)", ex.Message, error: true);
        }
    }

    private void RegisterHotkey()
    {
        Native.UnregisterHotKey(_messages.Handle, HotkeyId);
        KeyCombo combo = KeyCombo.ParseHotkey(_config.Hotkey);
        if (Native.RegisterHotKey(_messages.Handle, HotkeyId, (uint)combo.Modifiers | 0x4000, (uint)combo.VirtualKey))
        {
            _hotkeyStatus = $"Hotkey: {combo}";
        }
        else
        {
            _hotkeyStatus = $"Hotkey {combo} is used by another program";
            Notify($"{combo} is already used by another program",
                "Close that program's shortcut (for example Power Ops > Actions > Global shortcuts), or pick another \"hotkey\" in ring.json. Left-click the tray icon opens the ring meanwhile.", error: true);
        }
        _tray.Text = Truncate("Power Ring - " + _hotkeyStatus, 63);
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Native.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            ShowRing();
        }
        return IntPtr.Zero;
    }

    private void BuildMenu()
    {
        var menu = _tray.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Items.Add("Open ring", null, (_, _) => _dispatcher.BeginInvoke(ShowRing));
        var profiles = new System.Windows.Forms.ToolStripMenuItem("Profile");
        for (int i = 0; i < _config.Profiles.Count; i++)
        {
            int index = i;
            profiles.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem($"{i + 1}. {_config.Profiles[i].Name}", null, (_, _) => _dispatcher.BeginInvoke(() => SetProfile(index)))
            {
                Checked = _ring.Navigator.ProfileIndex == i,
            });
        }
        menu.Items.Add(profiles);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Edit ring.json", null, (_, _) => Open(_store.FilePath, edit: true));
        menu.Items.Add("Open settings folder", null, (_, _) => Open(_store.Directory, edit: false));
        menu.Items.Add("Reload ring.json", null, (_, _) => _dispatcher.BeginInvoke(() => Reload()));
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup()) { Checked = StartsWithWindows() });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem(_hotkeyStatus) { Enabled = false });
        menu.Items.Add("Exit", null, (_, _) => _dispatcher.BeginInvoke(() => Application.Current.Shutdown()));
    }

    private static void Open(string path, bool edit)
    {
        try
        {
            // Prefer VS Code for ring.json (schema autocompletion), then the default "edit" verb.
            if (edit && TryStart("code", $"\"{path}\"")) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = edit ? "edit" : "" });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    private static bool TryStart(string command, string args)
    {
        try { Process.Start(new ProcessStartInfo(command, args) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }); return true; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool StartsWithWindows()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    private static void ToggleStartup()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (key.GetValue(RunValue) is string) key.DeleteValue(RunValue);
        else key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
    }

    private void Notify(string title, string text, bool error)
    {
        _tray.BalloonTipTitle = Truncate(title, 63);
        _tray.BalloonTipText = Truncate(text, 255);
        _tray.BalloonTipIcon = error ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info;
        _tray.ShowBalloonTip(8000);
        Toast.Show(title + "\n" + text, 6000);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    public void Dispose()
    {
        _watcher.Dispose();
        Native.UnregisterHotKey(_messages.Handle, HotkeyId);
        _messages.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _ring.Close();
    }
}

/// <summary>The tray icon: a small ring drawn at runtime (no image file to ship).</summary>
internal static class TrayIcon
{
    public static System.Drawing.Icon Create()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var outer = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x3B, 0x82, 0xF6), 5);
            g.DrawEllipse(outer, 4, 4, 24, 24);
            using var dot = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xF3, 0xF4, 0xF6));
            g.FillEllipse(dot, 12, 12, 8, 8);
        }
        IntPtr handle = bitmap.GetHicon();
        using var temporary = System.Drawing.Icon.FromHandle(handle);
        var icon = (System.Drawing.Icon)temporary.Clone();
        Native.DestroyIcon(handle);
        return icon;
    }
}
