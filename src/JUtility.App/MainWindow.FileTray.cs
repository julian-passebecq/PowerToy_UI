using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JUtility.App.Services;
using JUtility.Core.Actions;
using JUtility.Core.Files;
using JUtility.Core.Models;
using Microsoft.Win32;

namespace JUtility.App;

// V2.3 File tray: files received by WhatsApp, Messenger or email land in Downloads (or a chosen folder); the tray
// lists the newest ones so they can be dragged or pasted into ChatGPT, Claude or Gemini. Three surfaces show the same
// list: the "File tray" module, a section at the top of the Sidebar, and a small flyout opened from the Quick Shelf
// (tray.show). Every click action is a catalog action run through the shared dispatcher; the row acts as the target.
// Nothing runs until a tray surface is shown or a tray action is used; file contents are read only on request.
public partial class MainWindow
{
    private const string FileTrayHeader = "File tray";
    private const int TrayThumbnailPixels = 96, TraySidebarRows = 5, TrayFlyoutRows = 8;

    private enum TraySurface
    {
        Page,
        Sidebar,
        Flyout,
    }

    private FileTraySettingsStore? _trayStore;
    private FileTraySettings? _traySettings;
    private string? _trayLoadError;
    private FileTrayService? _trayService;
    private IReadOnlyCollection<string> _trayDismissed = [];
    private FileTrayEntry? _trayTarget;
    private FileTrayEntry? _promptTrayFile;
    private readonly CancellationTokenSource _trayLifetime = new();
    private CancellationTokenSource? _trayWork;
    private readonly Dictionary<string, ImageSource?> _trayThumbnails = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Image>> _trayThumbnailWaiters = new(StringComparer.Ordinal);
    private readonly Queue<(string Path, string Key)> _trayThumbnailQueue = new();
    private bool _trayThumbnailWorker, _trayRenderQueued;
    private string _trayStatus = string.Empty;
    private ContentControl? _trayPageHost;
    private StackPanel? _traySidebarItems;
    private TextBlock? _traySidebarStatus;
    private FileTrayFlyoutWindow? _trayFlyout;
    private ComboBox? _promptTrayBox;
    private TextBox? _lastPromptBody;

    private void InitializeFileTray(Action<string, Action, Func<string?>?> register)
    {
        register(QuickActionCatalog.TrayShow, ToggleTrayFlyout, () => LoadTraySettings() ? null : TraySettingsError());
        register(QuickActionCatalog.TrayCopyLatest, () => CopyTrayFile(RequireTrayFile(latest: true)), TrayUnavailable);
        register(QuickActionCatalog.TrayCopyFile, () => CopyTrayFile(RequireTrayFile()), TrayUnavailable);
        register(QuickActionCatalog.TrayCopyText, () => CopyTrayText(RequireTrayFile()), TrayUnavailable);
        register(QuickActionCatalog.TrayCopyImage, () => CopyTrayImage(RequireTrayFile()), () => TrayUnavailable()
            ?? (_trayTarget is { Kind: not (FileTrayKind.Pdf or FileTrayKind.Image) } ? "Only images and PDFs can be copied as an image." : null));
        register(QuickActionCatalog.TrayOpen, () => OpenTrayFile(RequireTrayFile()), TrayUnavailable);
        register(QuickActionCatalog.TrayReveal, () => RevealTrayFile(RequireTrayFile()), TrayUnavailable);
        register(QuickActionCatalog.TrayMove, () => MoveTrayFile(RequireTrayFile()), () => TrayUnavailable()
            ?? (_viewModel.Projects.Any(x => !x.IsArchived) ? null : "Add a project in Repository Hub first."));
        register(QuickActionCatalog.TrayRemove, () => RemoveTrayFile(RequireTrayFile()), TrayUnavailable);

        AddTraySidebarSection();
        InitializePromptTray();
        SidebarPanel.IsVisibleChanged += (_, e) => { if (e.NewValue is true) QueueTrayRender(); };
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) QueueTrayRender(); };
        Activated += (_, _) => { if (TraySurfaceVisible()) QueueTrayRender(); }; // refreshes the "3 min ago" labels without a timer
        Closed += (_, _) =>
        {
            _trayLifetime.Cancel();
            _trayWork?.Cancel();
            _trayService?.Dispose();
            _trayFlyout?.Close();
        };
    }

    // ---------------- Settings and lifetime ----------------

    private bool LoadTraySettings()
    {
        if (_traySettings is not null) return true;
        _trayStore ??= new FileTraySettingsStore(_viewModel.DataDirectory);
        try
        {
            _traySettings = _trayStore.Load();
            _trayLoadError = null;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            _trayLoadError = ex.Message;
            return false;
        }
    }

    private string TraySettingsError() =>
        $"File tray settings could not be read, so the tray is off. {_trayStore?.FilePath} was not changed.\n\n{_trayLoadError}";

    private string? TrayUnavailable() => !LoadTraySettings() ? TraySettingsError()
        : _traySettings!.Enabled ? null
        : "The file tray is off. Turn it on in File tray > Settings.";

    /// <summary>Starts watching on first use in this session. Null when the tray is off or its settings are unreadable.</summary>
    private FileTrayService? EnsureTray()
    {
        if (_trayService is not null) return _trayService;
        if (!LoadTraySettings() || !_traySettings!.Enabled) return null;
        var service = new FileTrayService(_traySettings.ResolveFolders(DownloadsFolder()), _traySettings.MaxItems, _trayDismissed);
        service.Changed += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (ReferenceEquals(service, _trayService)) QueueTrayRender();
        }));
        _trayService = service;
        service.Start();
        return service;
    }

    private void RestartTray()
    {
        if (_trayService is not null)
        {
            _trayDismissed = _trayService.DismissedKeys;
            _trayService.Dispose();
            _trayService = null;
        }

        _trayThumbnails.Clear();
        if (TraySurfaceVisible()) EnsureTray();
        QueueTrayRender();
    }

    /// <summary>Edits a copy, validates and saves it, and only then replaces the live settings.</summary>
    private string? TryUpdateTraySettings(Action<FileTraySettings> change)
    {
        if (!LoadTraySettings() || _trayStore is null) return TraySettingsError();
        FileTraySettings candidate = FileTraySettingsStore.Copy(_traySettings!);
        try
        {
            change(candidate);
            _trayStore.Save(candidate);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return ex.Message;
        }

        _traySettings = candidate;
        return null;
    }

    // ---------------- Rendering ----------------

    private bool TrayPageVisible() => _trayPageHost is not null && IsVisible && _activeModule == FileTrayHeader && PowerOpsShell.Visibility == Visibility.Visible;

    private bool TraySidebarVisible() => _traySidebarItems is not null && IsVisible && SidebarPanel.Visibility == Visibility.Visible;

    private bool TraySurfaceVisible() => TrayPageVisible() || TraySidebarVisible() || _trayFlyout?.IsVisible == true;

    private void QueueTrayRender()
    {
        if (_trayRenderQueued) return;
        _trayRenderQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _trayRenderQueued = false;
            RenderTraySurfaces();
        }));
    }

    private void RenderTraySurfaces()
    {
        if (TrayPageVisible())
        {
            RenderTrayPage();
            RefreshSecondaryNavigation();
        }

        if (TraySidebarVisible()) RenderTraySidebar();
        if (_trayFlyout?.IsVisible == true) RenderTrayFlyout();
        if (_promptTrayBox is not null && _activeModule == "Prompt Builder" && _trayService is not null) RefreshPromptTrayBox();
        PruneTrayThumbnails();
    }

    private IReadOnlyList<FileTrayEntry> TrayItems() => _trayService?.Items ?? [];

    private IReadOnlyList<FileTrayEntry> FilteredTrayItems()
    {
        string filter = GetModuleFilter(FileTrayHeader), search = GetModuleSearch(FileTrayHeader).Trim();
        return TrayItems()
            .Where(x => filter == "all" || filter == "kind:" + x.Kind || filter == "source:" + x.SourceLabel)
            .Where(x => search.Length == 0 || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void AddTrayNavigation(Action<string, string, int> add)
    {
        IReadOnlyList<FileTrayEntry> items = TrayItems();
        add("all", "All files", items.Count);
        foreach (IGrouping<string, FileTrayEntry> group in items.GroupBy(x => x.SourceLabel, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            add("source:" + group.Key, group.Key, group.Count());
        }

        foreach (FileTrayKind kind in Enum.GetValues<FileTrayKind>())
        {
            int count = items.Count(x => x.Kind == kind);
            if (count > 0) add("kind:" + kind, FileTrayFormat.KindLabel(kind) + " files", count);
        }
    }

    private string TraySummary()
    {
        if (!LoadTraySettings()) return "Settings could not be read. The tray is off.";
        if (!_traySettings!.Enabled) return "The file tray is off. Nothing is watched.";
        FileTrayService? service = _trayService;
        if (service is null) return "Not started.";
        string folders = service.Folders.Count == 0 ? "no folder (add one in Settings)" : string.Join(", ", service.Folders.Select(x => x.Label));
        IReadOnlyList<FileTrayEntry> items = service.Items;
        string summary = $"Watching {folders}. " + (items.Count == 0
            ? "No recent PDF, image, Word or text file yet."
            : $"{items.Count} file(s), newest {FileTrayFormat.Age(DateTimeOffset.UtcNow, items[0].ArrivedUtc)}.");
        foreach (string problem in service.Problems) summary += "\n" + problem;
        return summary;
    }

    private void SetTrayStatus(string text)
    {
        _trayStatus = text;
        _viewModel.StatusText = text;
        foreach (TextBlock block in TrayStatusBlocks()) block.Text = text;
    }

    private IEnumerable<TextBlock> TrayStatusBlocks()
    {
        if (_trayPageHost?.Content is FrameworkElement page && page.Tag is TextBlock pageStatus) yield return pageStatus;
        if (_trayFlyout?.Status is { } flyoutStatus) yield return flyoutStatus;
    }

    private TextBlock NewTrayStatusBlock()
    {
        var block = new TextBlock { Text = _trayStatus, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x63, 0xCE)), Margin = new Thickness(0, 2, 0, 4) };
        AutomationProperties.SetLiveSetting(block, AutomationLiveSetting.Polite);
        return block;
    }

    private UIElement CreateFileTrayPage()
    {
        _trayPageHost = new ContentControl();
        return _trayPageHost;
    }

    private void RenderTrayPage()
    {
        if (_trayPageHost is null) return;
        FileTrayService? service = EnsureTray();
        var root = new DockPanel { Margin = new Thickness(14, 10, 14, 10) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(SessionButton("Refresh", () => { EnsureTray()?.Rescan(); QueueTrayRender(); }));
        buttons.Children.Add(SessionButton("Settings...", EditTraySettings));
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "File tray", FontSize = 20, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock
        {
            Text = "Files that arrived in Downloads and your chosen folders (WhatsApp, Messenger or email attachments). "
                + "Drag one into a chat, or copy it and press Ctrl+V there.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray,
        });
        header.Children.Add(titles);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var summary = new TextBlock { Text = TraySummary(), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(summary, "File tray summary");
        DockPanel.SetDock(summary, Dock.Top);
        root.Children.Add(summary);
        TextBlock status = NewTrayStatusBlock();
        DockPanel.SetDock(status, Dock.Top);
        root.Children.Add(status);
        root.Tag = status;

        var list = new StackPanel();
        AutomationProperties.SetName(list, "File tray items");
        if (service is null)
        {
            list.Children.Add(TrayOffPanel());
        }
        else
        {
            IReadOnlyList<FileTrayEntry> items = FilteredTrayItems();
            foreach (FileTrayEntry entry in items) list.Children.Add(TrayRow(entry, TraySurface.Page));
            if (items.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = TrayItems().Count == 0
                        ? "Nothing yet. New PDF, image (PNG, JPG, WebP, GIF, HEIC), Word and text files in the watched folders appear here."
                        : "No file matches this filter or search.",
                    TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 12, 0, 0),
                });
            }
        }

        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _trayPageHost.Content = root;
    }

    private UIElement TrayOffPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (!LoadTraySettings())
        {
            panel.Children.Add(new TextBlock { Text = TraySettingsError(), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick });
            panel.Children.Add(SessionButton("Retry", () => { _traySettings = null; RestartTray(); }));
            return panel;
        }

        panel.Children.Add(new TextBlock { Text = "The file tray is off: no folder is watched and nothing runs.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(SessionButton("Turn on", () =>
        {
            string? error = TryUpdateTraySettings(s => s.Enabled = true);
            if (error is not null) throw new InvalidOperationException(error);
            RestartTray();
        }));
        return panel;
    }

    // ---------------- Rows, thumbnails and drag ----------------

    private FrameworkElement TrayRow(FileTrayEntry entry, TraySurface surface)
    {
        bool compact = surface != TraySurface.Page;
        double size = compact ? 40 : 56;
        string age = FileTrayFormat.Age(DateTimeOffset.UtcNow, entry.ArrivedUtc);
        var row = new FileTrayRowBorder
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xDE, 0xE8)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 3, 0, 3),
            ToolTip = $"{entry.Path}\nDrag into a chat or a folder to copy it there.",
        };
        AutomationProperties.SetName(row, $"{entry.Name}, {entry.SourceLabel}, {age}");
        AutomationProperties.SetHelpText(row, $"{FileTrayFormat.KindLabel(entry.Kind)}, {FileTrayFormat.Size(entry.Length)}. Drag it into another app to copy it.");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(TrayThumbnail(entry, size));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll };
        text.Children.Add(new TextBlock { Text = entry.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = compact
                ? $"{entry.SourceLabel} · {age}"
                : $"{entry.SourceLabel} · {age} · {FileTrayFormat.KindLabel(entry.Kind)} · {FileTrayFormat.Size(entry.Length)}",
            Foreground = Brushes.DimGray, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        bool image = entry.Kind is FileTrayKind.Pdf or FileTrayKind.Image;
        if (compact)
        {
            buttons.Children.Add(TrayButton(QuickActionCatalog.TrayCopyFile, entry, surface, "Copy"));
            buttons.Children.Add(TrayButton(QuickActionCatalog.TrayCopyText, entry, surface, "Text"));
            if (image) buttons.Children.Add(TrayButton(QuickActionCatalog.TrayCopyImage, entry, surface, "Image"));
            buttons.Children.Add(TrayMoreButton(entry, surface));
            Grid.SetColumn(buttons, 1);
            Grid.SetColumnSpan(buttons, 2);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(buttons, 1);
            Grid.SetRowSpan(grid.Children[0], 2);
            buttons.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            buttons.Children.Add(TrayButton(QuickActionCatalog.TrayCopyFile, entry, surface));
            buttons.Children.Add(TrayButton(QuickActionCatalog.TrayCopyText, entry, surface));
            if (image) buttons.Children.Add(TrayButton(QuickActionCatalog.TrayCopyImage, entry, surface));
            buttons.Children.Add(TrayButton(QuickActionCatalog.TrayOpen, entry, surface, "Open"));
            buttons.Children.Add(TrayButton(QuickActionCatalog.TrayReveal, entry, surface));
            buttons.Children.Add(TrayButton(QuickActionCatalog.TrayMove, entry, surface, "Move..."));
            buttons.Children.Add(TrayPromptButton(entry, "Prompt"));
            buttons.Children.Add(TrayButton(QuickActionCatalog.TrayRemove, entry, surface, "Remove"));
            Grid.SetColumn(buttons, 2);
        }

        grid.Children.Add(buttons);
        row.Child = grid;
        AttachTrayDrag(row, entry, surface);
        return row;
    }

    private Button TrayButton(string actionId, FileTrayEntry entry, TraySurface surface, string? label = null)
    {
        QuickActionDefinition action = QuickActionCatalog.Get(actionId);
        var button = new Button { Content = label ?? action.Label, Margin = new Thickness(2), Padding = new Thickness(7, 2, 7, 2), ToolTip = action.Description };
        AutomationProperties.SetName(button, $"{action.Label}: {entry.Name}");
        button.Click += (_, _) => RunTrayAction(actionId, entry, surface);
        return button;
    }

    private Button TrayPromptButton(FileTrayEntry entry, string label)
    {
        var button = new Button { Content = label, Margin = new Thickness(2), Padding = new Thickness(7, 2, 7, 2), ToolTip = "Use this file for {{file}} and {{file_text}} in Prompt Builder." };
        AutomationProperties.SetName(button, $"Use in Prompt Builder: {entry.Name}");
        button.Click += (_, _) => UseTrayFileInPrompt(entry);
        return button;
    }

    private Button TrayMoreButton(FileTrayEntry entry, TraySurface surface)
    {
        var button = new Button { Content = "...", Margin = new Thickness(2), Padding = new Thickness(7, 2, 7, 2), ToolTip = "More actions" };
        AutomationProperties.SetName(button, $"More actions: {entry.Name}");
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            void Add(string header, Action action)
            {
                var item = new MenuItem { Header = header };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }

            Add("Open file", () => RunTrayAction(QuickActionCatalog.TrayOpen, entry, surface));
            Add("Show in folder", () => RunTrayAction(QuickActionCatalog.TrayReveal, entry, surface));
            Add("Move to project folder...", () => RunTrayAction(QuickActionCatalog.TrayMove, entry, surface));
            Add("Use in Prompt Builder", () => UseTrayFileInPrompt(entry));
            menu.Items.Add(new Separator());
            Add("Remove from tray", () => RunTrayAction(QuickActionCatalog.TrayRemove, entry, surface));
            menu.IsOpen = true;
        };
        return button;
    }

    /// <summary>The row is the target of the catalog action; surfaces never implement the action themselves.</summary>
    private void RunTrayAction(string actionId, FileTrayEntry entry, TraySurface surface)
    {
        _trayTarget = entry;
        try
        {
            RunQuickAction(actionId, surface == TraySurface.Flyout ? ActionSurface.QuickShelf : ActionSurface.FullUi);
        }
        finally
        {
            _trayTarget = null;
        }
    }

    private FrameworkElement TrayThumbnail(FileTrayEntry entry, double size)
    {
        var holder = new Grid { Width = size, Height = size, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        holder.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = entry.Kind switch { FileTrayKind.Pdf => "PDF", FileTrayKind.Image => "IMG", FileTrayKind.Document => "DOC", _ => "TXT" },
                FontSize = 10, Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        holder.Children.Add(image);
        string key = TrayThumbnailKey(entry);
        if (_trayThumbnails.TryGetValue(key, out ImageSource? cached))
        {
            image.Source = cached;
            return holder;
        }

        if (!_trayThumbnailWaiters.TryGetValue(key, out List<Image>? waiters)) _trayThumbnailWaiters[key] = waiters = [];
        waiters.Add(image);
        QueueTrayThumbnail(entry.Path, key);
        return holder;
    }

    private static string TrayThumbnailKey(FileTrayEntry entry) => $"{entry.Key}|{entry.LastWriteUtc.UtcTicks}|{entry.Length}";

    /// <summary>
    /// Thumbnails are made only for rows being shown, one at a time on a short-lived STA thread that ends when the queue
    /// is empty (no idle thread), then kept in memory while the file stays in the tray.
    /// </summary>
    private void QueueTrayThumbnail(string path, string key)
    {
        lock (_trayThumbnailQueue)
        {
            if (_trayThumbnailQueue.Any(x => x.Key == key)) return;
            _trayThumbnailQueue.Enqueue((path, key));
            if (_trayThumbnailWorker) return;
            _trayThumbnailWorker = true;
        }

        var worker = new Thread(TrayThumbnailWorker) { IsBackground = true, Name = "Power Ops tray thumbnails" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    private void TrayThumbnailWorker()
    {
        while (true)
        {
            (string Path, string Key) item;
            lock (_trayThumbnailQueue)
            {
                if (_trayThumbnailQueue.Count == 0 || _trayLifetime.IsCancellationRequested)
                {
                    _trayThumbnailQueue.Clear();
                    _trayThumbnailWorker = false;
                    return;
                }

                item = _trayThumbnailQueue.Dequeue();
            }

            BitmapSource? bitmap = FileTrayShell.Thumbnail(item.Path, TrayThumbnailPixels);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _trayThumbnails[item.Key] = bitmap;
                if (_trayThumbnailWaiters.Remove(item.Key, out List<Image>? waiters))
                {
                    foreach (Image image in waiters) image.Source = bitmap;
                }
            }));
        }
    }

    private void PruneTrayThumbnails()
    {
        var keep = TrayItems().Select(TrayThumbnailKey).ToHashSet(StringComparer.Ordinal);
        foreach (string key in _trayThumbnails.Keys.Where(x => !keep.Contains(x)).ToList()) _trayThumbnails.Remove(key);
        foreach (string key in _trayThumbnailWaiters.Keys.Where(x => !keep.Contains(x)).ToList()) _trayThumbnailWaiters.Remove(key);
    }

    private void AttachTrayDrag(FrameworkElement row, FileTrayEntry entry, TraySurface surface)
    {
        Point? start = null;
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // Pressing a button is a click, never the start of a drag.
            start = e.OriginalSource is DependencyObject source && FindAncestor<ButtonBase>(source) is null ? e.GetPosition(row) : null;
        };
        row.PreviewMouseLeftButtonUp += (_, _) => start = null;
        row.PreviewMouseMove += (_, e) =>
        {
            if (start is not Point origin || e.LeftButton != MouseButtonState.Pressed) return;
            Vector moved = e.GetPosition(row) - origin;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null;
            DragTrayFile(row, entry, surface);
        };
    }

    private void DragTrayFile(FrameworkElement row, FileTrayEntry entry, TraySurface surface)
    {
        if (!File.Exists(entry.Path))
        {
            _trayService?.Remove(entry.Path);
            SetTrayStatus($"{entry.Name} is no longer there. It was removed from the tray.");
            return;
        }

        bool previous = _suppressAutoHide;
        _suppressAutoHide = true;
        DragDropEffects effect;
        try
        {
            effect = FileTrayShell.DragFile(row, entry.Path);
        }
        finally
        {
            _suppressAutoHide = previous;
        }

        SetTrayStatus(effect == DragDropEffects.None ? "Drag cancelled." : $"Dropped {entry.Name}.");
        if (effect != DragDropEffects.None && surface == TraySurface.Flyout) _trayFlyout?.Hide();
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T) node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }

    // ---------------- Sidebar section ----------------

    private void AddTraySidebarSection()
    {
        if (SidebarPanel.Content is not StackPanel sidebar) return;
        var section = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var header = new DockPanel();
        var open = new Button { Content = "Open", HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(open, "Open the file tray");
        open.Click += (_, _) => OpenTrayModule();
        DockPanel.SetDock(open, Dock.Right);
        header.Children.Add(open);
        header.Children.Add(new TextBlock { Text = "File tray", FontWeight = FontWeights.SemiBold, FontSize = 14 });
        section.Children.Add(header);
        _traySidebarStatus = new TextBlock { Foreground = Brushes.DimGray, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        section.Children.Add(_traySidebarStatus);
        _traySidebarItems = new StackPanel();
        AutomationProperties.SetName(_traySidebarItems, "File tray items");
        section.Children.Add(_traySidebarItems);
        int index = Math.Min(2, sidebar.Children.Count);
        sidebar.Children.Insert(index, section);
        sidebar.Children.Insert(index + 1, new Separator { Margin = new Thickness(0, 10, 0, 0) });
    }

    private void RenderTraySidebar()
    {
        if (_traySidebarItems is null || _traySidebarStatus is null) return;
        FileTrayService? service = EnsureTray();
        _traySidebarItems.Children.Clear();
        IReadOnlyList<FileTrayEntry> items = TrayItems();
        _traySidebarStatus.Text = service is null ? TraySummary()
            : items.Count == 0 ? "Nothing received yet in " + string.Join(", ", service.Folders.Select(x => x.Label)) + "."
            : $"Drag a file into a chat, or Copy and press Ctrl+V. {items.Count} file(s).";
        foreach (FileTrayEntry entry in items.Take(TraySidebarRows)) _traySidebarItems.Children.Add(TrayRow(entry, TraySurface.Sidebar));
    }

    private void OpenTrayModule()
    {
        BringToFront();
        if (_viewModel.ViewMode == WorkspaceViewMode.Sidebar) SetViewMode(WorkspaceViewMode.Compact);
        ShowModule("tray");
    }

    // ---------------- Quick Shelf flyout ----------------

    private void ToggleTrayFlyout()
    {
        if (_trayFlyout?.IsVisible == true)
        {
            _trayFlyout.Hide();
            return;
        }

        if (_trayFlyout is not { } flyout)
        {
            flyout = new FileTrayFlyoutWindow();
            flyout.OpenRequested += (_, _) => { flyout.Hide(); OpenTrayModule(); };
            flyout.SettingsRequested += (_, _) => { flyout.Hide(); OpenTrayModule(); EditTraySettings(); };
            _trayFlyout = flyout;
        }

        RenderTrayFlyout();
        flyout.Show();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (flyout.IsVisible) WindowPlacementService.MoveNearCursor(flyout); }));
    }

    private void RenderTrayFlyout()
    {
        if (_trayFlyout is null) return;
        FileTrayService? service = EnsureTray();
        IReadOnlyList<FileTrayEntry> items = TrayItems();
        _trayFlyout.Summary.Text = service is null ? TraySummary()
            : items.Count == 0 ? "Nothing received yet in " + string.Join(", ", service.Folders.Select(x => x.Label)) + "."
            : "Drag a file into the chat, or Copy then Ctrl+V.";
        _trayFlyout.Status.Text = _trayStatus;
        _trayFlyout.Items.Children.Clear();
        foreach (FileTrayEntry entry in items.Take(TrayFlyoutRows)) _trayFlyout.Items.Children.Add(TrayRow(entry, TraySurface.Flyout));
    }

    // ---------------- Actions (one implementation each, reached through the dispatcher) ----------------

    private FileTrayEntry RequireTrayFile(bool latest = false)
    {
        FileTrayEntry entry = (latest ? null : _trayTarget) ?? EnsureTray()?.Latest
            ?? throw new InvalidOperationException("The tray is empty: no recent PDF, image, Word or text file in the watched folders.");
        if (File.Exists(entry.Path)) return entry;
        _trayService?.Remove(entry.Path);
        throw new FileNotFoundException($"{entry.Name} is no longer in {entry.SourceLabel}. It was removed from the tray.");
    }

    private void CopyTrayFile(FileTrayEntry entry)
    {
        FileTrayShell.CopyFile(entry.Path);
        SetTrayStatus($"Copied {entry.Name} as a file. Press Ctrl+V in the chat (or in a folder).");
    }

    private void CopyTrayText(FileTrayEntry entry)
    {
        CancellationToken token = RestartTrayWork();
        SetTrayStatus($"Reading text from {entry.Name}...");
        _ = CopyTrayTextAsync(entry, token);
    }

    private async Task CopyTrayTextAsync(FileTrayEntry entry, CancellationToken cancellation)
    {
        try
        {
            (string text, string method) = await ExtractTrayTextAsync(entry, cancellation);
            if (cancellation.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(text))
            {
                SetTrayStatus($"No text found in {entry.Name} ({method}).");
                return;
            }

            if (CopyText(text, "Text copied")) SetTrayStatus($"Copied {text.Length:N0} characters from {entry.Name} ({method}).");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetTrayStatus($"Could not read text from {entry.Name}.");
            ShowOwnedMessage($"Could not read text from {entry.Name}.\n\n{ex.Message}", "File tray", MessageBoxImage.Warning);
        }
        finally
        {
            ReleaseTrayWorkMemory();
        }
    }

    /// <summary>Text for Copy text and {{file_text}}. Only runs when the user asks; the result is never stored.</summary>
    private async Task<(string Text, string Method)> ExtractTrayTextAsync(FileTrayEntry entry, CancellationToken cancellation)
    {
        switch (entry.Kind)
        {
            case FileTrayKind.Text:
                return (await Task.Run(() => FileTrayText.ReadPlainText(entry.Path), cancellation), "text file");
            case FileTrayKind.Document:
                return (await Task.Run(() => FileTrayText.ExtractDocx(entry.Path), cancellation), "Word document");
            case FileTrayKind.Pdf:
                string layer = await Task.Run(() => FileTrayPdfText.Extract(entry.Path, cancellation), cancellation);
                if (FileTrayText.HasText(layer)) return (layer, "PDF text");
                SetTrayStatus($"{entry.Name} has no text layer; running Windows OCR...");
                return (FileTrayText.Finish(await FileTrayWinRt.OcrPdfAsync(entry.Path, cancellation), false), "Windows OCR, scanned PDF");
            default:
                return (FileTrayText.Finish(await FileTrayWinRt.OcrImageAsync(entry.Path, cancellation), false), "Windows OCR");
        }
    }

    private void CopyTrayImage(FileTrayEntry entry)
    {
        if (entry.Kind is not (FileTrayKind.Pdf or FileTrayKind.Image)) throw new InvalidOperationException("Only images and PDFs can be copied as an image.");
        CancellationToken token = RestartTrayWork();
        SetTrayStatus(entry.Kind == FileTrayKind.Pdf ? $"Rendering page 1 of {entry.Name}..." : $"Loading {entry.Name}...");
        _ = CopyTrayImageAsync(entry, token);
    }

    private async Task CopyTrayImageAsync(FileTrayEntry entry, CancellationToken cancellation)
    {
        try
        {
            BitmapSource image;
            byte[] png;
            if (entry.Kind == FileTrayKind.Pdf)
            {
                // Width in device-independent pixels (Windows applies the display scale): readable, and small on the clipboard.
                png = await FileTrayWinRt.RenderPdfPagePngAsync(entry.Path, 0, 1200);
                image = FileTrayShell.DecodePng(png);
            }
            else
            {
                try
                {
                    image = await Task.Run(() => FileTrayShell.LoadImage(entry.Path), cancellation);
                    png = await Task.Run(() => FileTrayShell.EncodePng(image), cancellation);
                }
                catch (Exception ex) when (ex is NotSupportedException or FileFormatException or System.Runtime.InteropServices.COMException)
                {
                    // WPF has no decoder for this format (HEIC): let Windows imaging convert it.
                    png = await FileTrayWinRt.ConvertImageToPngAsync(entry.Path, FileTrayShell.MaxImageSide);
                    image = FileTrayShell.DecodePng(png);
                }
            }

            if (cancellation.IsCancellationRequested) return;
            FileTrayShell.CopyImage(image, png);
            SetTrayStatus(entry.Kind == FileTrayKind.Pdf
                ? $"Copied page 1 of {entry.Name} as an image ({image.PixelWidth}x{image.PixelHeight}). Press Ctrl+V in the chat."
                : $"Copied {entry.Name} as an image ({image.PixelWidth}x{image.PixelHeight}). Press Ctrl+V in the chat.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            string hint = entry.Path.EndsWith(".heic", StringComparison.OrdinalIgnoreCase)
                ? "\n\nHEIC photos need the HEIF Image Extensions from the Microsoft Store."
                : string.Empty;
            SetTrayStatus($"Could not copy {entry.Name} as an image.");
            ShowOwnedMessage($"Could not copy {entry.Name} as an image.\n\n{ex.Message}{hint}", "File tray", MessageBoxImage.Warning);
        }
        finally
        {
            ReleaseTrayWorkMemory();
        }
    }

    /// <summary>
    /// Rendered pages and decoded images sit on the large-object heap, which only a full collection frees, and the
    /// Windows OCR/PDF objects are released by finalizers. Release both once when on-demand work ends instead of
    /// keeping them in the idle process.
    /// </summary>
    private static void ReleaseTrayWorkMemory()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private CancellationToken RestartTrayWork()
    {
        _trayWork?.Cancel();
        _trayWork = CancellationTokenSource.CreateLinkedTokenSource(_trayLifetime.Token);
        return _trayWork.Token;
    }

    private void OpenTrayFile(FileTrayEntry entry)
    {
        Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
        SetTrayStatus($"Opened {entry.Name}.");
    }

    private void RevealTrayFile(FileTrayEntry entry)
    {
        Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + entry.Path + "\"", UseShellExecute = true });
        SetTrayStatus($"Showing {entry.Name} in Explorer.");
    }

    private void RemoveTrayFile(FileTrayEntry entry)
    {
        if (_trayService?.Dismiss(entry.Path) == true) SetTrayStatus($"Removed {entry.Name} from the tray. The file was not deleted.");
    }

    private void MoveTrayFile(FileTrayEntry entry)
    {
        BringToFront();
        List<ProjectEntry> projects = _viewModel.Projects.Where(x => !x.IsArchived).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (projects.Count == 0) throw new InvalidOperationException("Add a project in Repository Hub first.");
        LoadTraySettings();

        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = $"Move {entry.Name} from {entry.SourceLabel} into a project's folder. The file is moved, not copied, and nothing is deleted or overwritten.",
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock { Text = "Project", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2) });
        var projectBox = new ComboBox { ItemsSource = projects, DisplayMemberPath = nameof(ProjectEntry.Name), SelectedIndex = 0 };
        AutomationProperties.SetName(projectBox, "Project");
        body.Children.Add(projectBox);
        body.Children.Add(new TextBlock { Text = "Folder", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2) });
        var folderText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        body.Children.Add(folderText);
        string? folder = null;
        void Sync()
        {
            folder = projectBox.SelectedItem is ProjectEntry project ? _traySettings?.ProjectFolder(project.Id) : null;
            folderText.Text = folder ?? "No folder chosen for this project yet. Choose it once; Power Ops remembers it.";
        }

        projectBox.SelectionChanged += (_, _) => Sync();
        Sync();
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        Window dialog = SessionDialogWindow("Move to project folder", body);
        dialog.Height = 360;
        body.Children.Add(SessionButton("Choose folder...", () =>
        {
            var picker = new OpenFolderDialog { Title = "Project folder", Multiselect = false };
            if (folder is not null && Directory.Exists(folder)) picker.InitialDirectory = folder;
            if (picker.ShowDialog(dialog) == true)
            {
                folder = picker.FolderName;
                folderText.Text = folder;
            }
        }));
        body.Children.Add(error);
        string? destination = null;
        body.Children.Add(SessionButton("Move", () =>
        {
            if (projectBox.SelectedItem is not ProjectEntry project || folder is null || !Directory.Exists(folder))
            {
                error.Text = "Choose an existing folder for the project first.";
                return;
            }

            if (string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), Path.GetDirectoryName(entry.Path), StringComparison.OrdinalIgnoreCase))
            {
                error.Text = "The file is already in that folder.";
                return;
            }

            string target = UniqueDestination(folder, entry.Name);
            // Update the tray first so the watcher's "deleted" event for the old path does not drop the entry.
            bool tracked = _trayService?.Relocate(entry.Path, target, "Moved to " + project.Name) == true;
            try
            {
                File.Move(entry.Path, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (tracked) _trayService?.Relocate(target, entry.Path, entry.SourceLabel);
                error.Text = ex.Message;
                return;
            }

            destination = target;
            string? saveError = TryUpdateTraySettings(s => s.RememberProjectFolder(project.Id, folder));
            if (saveError is not null) _viewModel.StatusText = "The folder was not remembered: " + saveError;
            dialog.DialogResult = true;
        }));

        if (SessionDialog(dialog) && destination is not null) SetTrayStatus($"Moved {entry.Name} to {Path.GetDirectoryName(destination)}.");
    }

    private static string UniqueDestination(string folder, string name)
    {
        string candidate = Path.Combine(folder, name);
        string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
        for (int number = 2; File.Exists(candidate) || Directory.Exists(candidate); number++)
        {
            candidate = Path.Combine(folder, $"{stem} ({number}){extension}");
        }

        return candidate;
    }

    // ---------------- Settings dialog ----------------

    private void EditTraySettings() => SessionAction(() =>
    {
        if (!LoadTraySettings())
        {
            ShowOwnedMessage(TraySettingsError(), "File tray", MessageBoxImage.Warning);
            return;
        }

        BringToFront();
        FileTraySettings settings = _traySettings!;
        string downloads = DownloadsFolder();
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = "The tray lists the newest PDF, image, Word and text files in these folders (top level only). "
                + "WhatsApp Desktop and Messenger save to Downloads unless you picked another folder in their settings: add that folder here. "
                + "Only these settings are saved; file names and contents are never saved or exported.",
            TextWrapping = TextWrapping.Wrap,
        });
        var enabled = new CheckBox { Content = "Watch these folders (off: nothing is watched and nothing runs)", IsChecked = settings.Enabled, Margin = new Thickness(0, 12, 0, 4) };
        var watchDownloads = new CheckBox { Content = $"Downloads ({downloads})", IsChecked = settings.WatchDownloads, Margin = new Thickness(0, 4, 0, 8) };
        body.Children.Add(enabled);
        body.Children.Add(watchDownloads);
        body.Children.Add(new TextBlock { Text = "Extra folders", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
        var rows = new StackPanel();
        var editors = new List<(TextBox Label, string Path, UIElement Row)>();
        void AddRow(string label, string path)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labelBox = new TextBox { Text = label, MaxLength = FileTraySettings.MaxLabelLength, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Name shown on tray items, e.g. WhatsApp" };
            AutomationProperties.SetName(labelBox, "Folder label");
            var pathText = new TextBlock { Text = path, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = path };
            var remove = new Button { Content = "Remove", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2) };
            AutomationProperties.SetName(remove, "Remove folder " + path);
            Grid.SetColumn(pathText, 1);
            Grid.SetColumn(remove, 2);
            grid.Children.Add(labelBox);
            grid.Children.Add(pathText);
            grid.Children.Add(remove);
            var entry = (labelBox, path, (UIElement)grid);
            editors.Add(entry);
            rows.Children.Add(grid);
            remove.Click += (_, _) => { editors.Remove(entry); rows.Children.Remove(grid); };
        }

        foreach (FileTrayFolder folder in settings.ExtraFolders) AddRow(folder.Label, folder.Path);
        body.Children.Add(rows);
        Window dialog = SessionDialogWindow("File tray settings", body);
        dialog.Height = 560;
        body.Children.Add(SessionButton("+ Add folder...", () =>
        {
            if (editors.Count >= FileTraySettings.MaxExtraFolders) return;
            var picker = new OpenFolderDialog { Title = "Folder to watch", Multiselect = false };
            if (picker.ShowDialog(dialog) == true) AddRow(Path.GetFileName(picker.FolderName.TrimEnd('\\')), picker.FolderName);
        }));
        var count = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        count.Children.Add(new TextBlock { Text = "Keep the newest", VerticalAlignment = VerticalAlignment.Center });
        var maxItems = new TextBox { Text = settings.MaxItems.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 48, Margin = new Thickness(6, 0, 6, 0), MaxLength = 3 };
        AutomationProperties.SetName(maxItems, "Number of files to keep");
        count.Children.Add(maxItems);
        count.Children.Add(new TextBlock { Text = $"files ({FileTrayList.MinCapacity}-{FileTrayList.MaxCapacity})", VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(count);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(error);
        body.Children.Add(SessionButton("Save", () =>
        {
            if (!int.TryParse(maxItems.Text.Trim(), out int max))
            {
                error.Text = "Enter a number of files.";
                return;
            }

            string? failure = TryUpdateTraySettings(s =>
            {
                s.Enabled = enabled.IsChecked == true;
                s.WatchDownloads = watchDownloads.IsChecked == true;
                s.MaxItems = max;
                s.ExtraFolders = editors.Select(x => new FileTrayFolder { Path = x.Path, Label = x.Label.Text.Trim() }).ToList();
            });
            if (failure is null) dialog.DialogResult = true;
            else error.Text = failure;
        }));

        if (SessionDialog(dialog))
        {
            RestartTray();
            SetTrayStatus("File tray settings saved.");
        }
    });

    // ---------------- Prompt Builder: {{file}} and {{file_text}} ----------------

    private void InitializePromptTray()
    {
        if (PromptTrayHost is null) return;
        var row = new DockPanel { Margin = new Thickness(3, 6, 3, 0) };
        var label = new TextBlock { Text = "Tray file", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Width = 70 };
        row.Children.Add(label);
        var inserts = new WrapPanel();
        foreach (string variable in FileTrayPrompt.BuiltIns)
        {
            string placeholder = FileTrayPrompt.Placeholder(variable);
            var button = new Button { Content = "Insert " + placeholder, Margin = new Thickness(3, 0, 0, 0), ToolTip = variable == FileTrayPrompt.FileVariable
                ? "Placeholder for the tray file's name."
                : "Placeholder for the tray file's text (PDF text, Word, text file, or Windows OCR). It is read when you compose." };
            AutomationProperties.SetName(button, "Insert " + placeholder);
            button.Click += (_, _) => InsertPromptPlaceholder(placeholder);
            inserts.Children.Add(button);
        }

        DockPanel.SetDock(inserts, Dock.Right);
        row.Children.Add(inserts);
        _promptTrayBox = new ComboBox { DisplayMemberPath = nameof(FileTrayEntry.Name), MinWidth = 120, ToolTip = "The file used for {{file}} and {{file_text}}. Default: the newest tray file." };
        AutomationProperties.SetName(_promptTrayBox, "Tray file for prompt placeholders");
        _promptTrayBox.DropDownOpened += (_, _) => { EnsureTray(); RefreshPromptTrayBox(); };
        _promptTrayBox.SelectionChanged += (_, _) => { if (_promptTrayBox.SelectedItem is FileTrayEntry entry) _promptTrayFile = entry; };
        row.Children.Add(_promptTrayBox);
        PromptTrayHost.Content = row;
        PromptModuleList.AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (e.NewFocus is TextBox box && AutomationProperties.GetName(box) == "Prompt module body") _lastPromptBody = box;
        }), true);
    }

    private void RefreshPromptTrayBox()
    {
        if (_promptTrayBox is null) return;
        IReadOnlyList<FileTrayEntry> items = TrayItems();
        FileTrayEntry? chosen = _promptTrayFile is not null ? items.FirstOrDefault(x => x.Key == _promptTrayFile.Key) ?? _promptTrayFile : null;
        List<FileTrayEntry> choices = items.ToList();
        if (chosen is not null && !choices.Contains(chosen)) choices.Insert(0, chosen);
        _promptTrayBox.ItemsSource = choices;
        _promptTrayBox.SelectedItem = chosen ?? choices.FirstOrDefault();
    }

    private void UseTrayFileInPrompt(FileTrayEntry entry)
    {
        _promptTrayFile = entry;
        BringToFront();
        if (_viewModel.ViewMode == WorkspaceViewMode.Sidebar) SetViewMode(WorkspaceViewMode.Compact);
        ShowModule("prompts");
        RefreshPromptTrayBox();
        SetTrayStatus($"Prompt Builder uses {entry.Name} for {{{{file}}}} and {{{{file_text}}}}.");
    }

    private void InsertPromptPlaceholder(string placeholder)
    {
        if (_lastPromptBody is not { IsLoaded: true } box)
        {
            _viewModel.StatusText = "Click in a prompt module's text first, then insert the placeholder.";
            return;
        }

        box.Focus();
        box.SelectedText = placeholder;
        box.CaretIndex = box.SelectionStart + placeholder.Length;
        box.SelectionLength = 0;
        _viewModel.RefreshPromptVariables();
        _viewModel.StatusText = $"Inserted {placeholder}";
    }

    /// <summary>
    /// Resolves {{file}} / {{file_text}} before composing. The text is read only when a module uses {{file_text}}.
    /// False when reading failed (already reported).
    /// </summary>
    private async Task<bool> PreparePromptTrayVariablesAsync()
    {
        _viewModel.PromptFileVariables = FileTrayPrompt.Variables(null, null);
        if (!FileTrayPrompt.UsesFile(_viewModel.PromptModules)) return true;
        FileTrayEntry? entry = _promptTrayFile ?? EnsureTray()?.Latest;
        if (entry is null || !File.Exists(entry.Path))
        {
            _viewModel.StatusText = "{{file}} stays as is: no tray file (the tray is empty or off, or the file moved).";
            return true;
        }

        string? text = null;
        if (FileTrayPrompt.NeedsText(_viewModel.PromptModules))
        {
            _viewModel.StatusText = $"Reading text from {entry.Name}...";
            try
            {
                text = (await ExtractTrayTextAsync(entry, RestartTrayWork())).Text;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                ShowOwnedMessage($"Could not read text from {entry.Name} for {{{{file_text}}}}.\n\n{ex.Message}", "Prompt Builder", MessageBoxImage.Warning);
                return false;
            }
        }

        _viewModel.PromptFileVariables = FileTrayPrompt.Variables(entry, text);
        return true;
    }
}

/// <summary>A tray row exposed to UI Automation as a named group (a plain Border has no automation peer).</summary>
internal sealed class FileTrayRowBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(FileTrayRowBorder owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => "FileTrayItem";
    }
}

/// <summary>
/// Small list opened from the Quick Shelf (tray.show). Like the Shelf it is a no-activate tool window: clicking Copy
/// or dragging a file does not take focus from the chat, so Ctrl+V works right away. Presentation only.
/// </summary>
internal sealed class FileTrayFlyoutWindow : Window
{
    private const int GwlExStyle = -20, WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000;

    public FileTrayFlyoutWindow()
    {
        Title = "Power Ops File tray";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 420;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xD0, 0xDC));
        BorderThickness = new Thickness(1);
        AutomationProperties.SetName(this, "Power Ops File tray");

        var root = new DockPanel { Margin = new Thickness(10) };
        var header = new DockPanel();
        var close = new Button { Content = "Close", Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        AutomationProperties.SetName(close, "Close file tray");
        close.Click += (_, _) => Hide();
        var settings = new Button { Content = "Settings", Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        AutomationProperties.SetName(settings, "File tray settings");
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var open = new Button { Content = "Open in Power Ops", Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        AutomationProperties.SetName(open, "Open the file tray in Power Ops");
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(open);
        buttons.Children.Add(settings);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        header.Children.Add(new TextBlock { Text = "File tray", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        Summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(Summary, Dock.Top);
        root.Children.Add(Summary);
        Status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x63, 0xCE)), FontSize = 11 };
        AutomationProperties.SetLiveSetting(Status, AutomationLiveSetting.Polite);
        DockPanel.SetDock(Status, Dock.Top);
        root.Children.Add(Status);
        Items = new StackPanel();
        AutomationProperties.SetName(Items, "File tray items");
        root.Children.Add(new ScrollViewer { Content = Items, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Hide();
            }
        };
        SourceInitialized += (_, _) =>
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(GetWindowLongPtr(handle, GwlExStyle).ToInt64() | WsExToolWindow | WsExNoActivate));
        };
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? SettingsRequested;

    public TextBlock Summary { get; }
    public TextBlock Status { get; }
    public StackPanel Items { get; }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
