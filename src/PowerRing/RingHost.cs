using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using PowerRing.Core;

namespace PowerRing;

// Owns everything that lives while Power Ring runs: ring.json (+ reload on save), the global hotkey, the tray icon, the
// ring window and, when a board has a clipboard/images table, the clipboard listener (memory only, never on disk).
// Idle cost: one message-only window, one FileSystemWatcher and one wait handle; no timers, no polling.
internal sealed class RingHost : IDisposable, IBoardSource
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly ClipHistory _texts = new();
    private readonly List<ClipImage> _images = [];
    private readonly RingNotesStore _notes;
    private bool _listening;
    private int _imageKeep = 9;
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
        _notes = new RingNotesStore(_store.Directory);
        WebIcons.Initialize(_store.Directory, _dispatcher);
        _ring = CreateRing(_config);
        UpdateClipboardListener();

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
        var ring = new RingWindow(config, this);
        ring.Invoked += (_, item) =>
        {
            if (RingActions.Of(item) == RingActions.RingSettings) RingSettings(item.Target);
            else ActionRunner.Run(item, _dispatcher);
        };
        return ring;
    }

    /// <summary>The "ring-settings" action: Power Ring's own settings, reachable from the ring itself.</summary>
    private void RingSettings(string? target)
    {
        switch (target?.Trim().ToLowerInvariant())
        {
            case "folder": Open(_store.Directory, edit: false); break;
            case "reload": Reload(); break;
            case "guide": Open(Path.Combine(_store.Directory, RingConfigStore.GuideFileName), edit: true); break;
            default: Open(_store.FilePath, edit: true); break;
        }
    }

    /// <summary>Tray menu edits (show/hide a workspace, add a preset) go through validation and keep ring.json.bak.</summary>
    private void Change(Action<RingConfig> change, string done)
    {
        try
        {
            RingConfig copy = RingConfigs.Parse(RingConfigs.Serialize(_config));
            change(copy);
            _store.Save(copy);
            Reload(announce: false);
            Toast.Show(done + " (previous ring.json kept as ring.json.bak)", 2500);
        }
        catch (Exception ex) when (ex is RingConfigException or IOException or UnauthorizedAccessException)
        {
            Notify("ring.json was not changed", ex.Message, error: true);
        }
    }

    private void ApplyLayout(string path)
    {
        try
        {
            _store.ApplyLayout(path);
            Reload(announce: false);
            Toast.Show($"Layout \"{Path.GetFileNameWithoutExtension(path)}\" active (previous ring.json kept as ring.json.bak)", 2500);
        }
        catch (Exception ex) when (ex is RingConfigException or IOException or UnauthorizedAccessException)
        {
            Notify("Layout not applied", ex.Message, error: true);
        }
    }

    // ---------------------------------------------------------------- board source (clipboard, images, notes)

    public IReadOnlyList<ClipText> Texts => _texts.Items;
    public IReadOnlyList<ClipImage> Images => _images;
    public RingNotesStore Notes => _notes;

    public void CopyText(string text) => ActionRunner.SetClipboard(() => Clipboard.SetText(text));

    public void CopyImage(ClipImage image)
    {
        var decoder = new PngBitmapDecoder(new MemoryStream(image.Png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        ActionRunner.SetClipboard(() => Clipboard.SetImage(decoder.Frames[0]));
    }

    /// <summary>Listen to the clipboard only when some board shows copies; stop as soon as none does.</summary>
    private void UpdateClipboardListener()
    {
        List<RingTable> tables = _config.Profiles.Where(p => p.IsBoard).SelectMany(p => p.Tables ?? []).ToList();
        RingTable? text = tables.FirstOrDefault(t => t.Kind.Equals(RingTableKinds.Clipboard, StringComparison.OrdinalIgnoreCase));
        RingTable? images = tables.FirstOrDefault(t => t.Kind.Equals(RingTableKinds.Images, StringComparison.OrdinalIgnoreCase));
        _texts.Capacity = text?.Keep ?? 20;
        _imageKeep = images?.Keep ?? 9;
        bool wanted = text is not null || images is not null;
        if (wanted && !_listening) _listening = AddClipboardFormatListener(_messages.Handle);
        else if (!wanted && _listening) { RemoveClipboardFormatListener(_messages.Handle); _listening = false; _texts.Clear(); _images.Clear(); }
    }

    private void OnClipboardChanged()
    {
        try
        {
            // Password managers and other private sources ask viewers to ignore their copies: respect it.
            if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing") || Clipboard.ContainsData("Clipboard Viewer Ignore")) return;
            if (Clipboard.GetData("CanIncludeInClipboardHistory") is MemoryStream flag && flag.Length >= 4 && BitConverter.ToInt32(flag.ToArray(), 0) == 0) return;
            if (Clipboard.ContainsText())
            {
                if (_texts.Add(Clipboard.GetText(), DateTimeOffset.Now)) _ring.BoardChanged();
                return;
            }
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource image) AddImage(image);
        }
        catch (Exception ex) when (ex is COMException or ExternalException or OutOfMemoryException or ArgumentException) { }
    }

    /// <summary>Keeps a compressed copy (PNG) and a small thumbnail, encoded off the UI thread.</summary>
    private void AddImage(BitmapSource image)
    {
        var copy = new WriteableBitmap(image);
        copy.Freeze();
        DateTimeOffset at = DateTimeOffset.Now;
        Task.Run(() =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(copy));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            double factor = Math.Min(1, 240.0 / Math.Max(copy.PixelWidth, copy.PixelHeight));
            var thumb = new TransformedBitmap(copy, new ScaleTransform(factor, factor));
            thumb.Freeze();
            return new ClipImage(thumb, stream.ToArray(), copy.PixelWidth, copy.PixelHeight, at);
        }).ContinueWith(task =>
        {
            if (task.IsFaulted) return;
            ClipImage clip = task.Result;
            _images.RemoveAll(x => x.Png.AsSpan().SequenceEqual(clip.Png));
            _images.Insert(0, clip);
            if (_images.Count > _imageKeep) _images.RemoveRange(_imageKeep, _images.Count - _imageKeep);
            _ring.BoardChanged();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr window);

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
            UpdateClipboardListener();
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
        else if (message == WmClipboardUpdate)
        {
            // Read after the copying program has finished with the clipboard.
            _dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, OnClipboardChanged);
        }
        return IntPtr.Zero;
    }

    private void BuildMenu()
    {
        var menu = _tray.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Items.Add("Open ring", null, (_, _) => _dispatcher.BeginInvoke(ShowRing));
        var profiles = new System.Windows.Forms.ToolStripMenuItem("Profile");
        for (int i = 0; i < _ring.Navigator.Profiles.Count; i++)
        {
            int index = i;
            profiles.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem($"{i + 1}. {_ring.Navigator.Profiles[i].Name}", null, (_, _) => _dispatcher.BeginInvoke(() => SetProfile(index)))
            {
                Checked = _ring.Navigator.ProfileIndex == i,
            });
        }
        menu.Items.Add(profiles);
        var workspaces = new System.Windows.Forms.ToolStripMenuItem("Workspaces");
        foreach (RingProfile profile in _config.Profiles)
        {
            string id = profile.Id;
            workspaces.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem(profile.Name, null, (_, _) => _dispatcher.BeginInvoke(() =>
                Change(c => { RingProfile p = c.Profiles.First(x => x.Id == id); p.Enabled = !p.Enabled; }, $"{profile.Name} {(profile.Enabled ? "hidden" : "shown")}")))
            {
                Checked = profile.Enabled,
                ToolTipText = "Untick to hide this workspace (it stays in ring.json).",
            });
        }
        List<RingProfile> missing = RingDefaults.Presets().Where(p => !_config.Profiles.Any(x => x.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count > 0 && _config.Profiles.Count < RingConfigs.MaxProfiles)
        {
            workspaces.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
            foreach (RingProfile preset in missing)
            {
                RingProfile captured = preset;
                workspaces.DropDownItems.Add($"Add preset: {preset.Name}", null, (_, _) => _dispatcher.BeginInvoke(() =>
                    Change(c => c.Profiles.Add(captured), $"{captured.Name} added")));
            }
        }
        menu.Items.Add(workspaces);
        var layouts = new System.Windows.Forms.ToolStripMenuItem("Layouts");
        foreach (string file in _store.Layouts())
        {
            string path = file;
            layouts.DropDownItems.Add(Path.GetFileNameWithoutExtension(file), null, (_, _) => _dispatcher.BeginInvoke(() => ApplyLayout(path)));
        }
        if (layouts.DropDownItems.Count > 0) layouts.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        layouts.DropDownItems.Add("Open layouts folder", null, (_, _) => { System.IO.Directory.CreateDirectory(_store.LayoutsDirectory); Open(_store.LayoutsDirectory, edit: false); });
        menu.Items.Add(layouts);
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
        if (_listening) RemoveClipboardFormatListener(_messages.Handle);
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
