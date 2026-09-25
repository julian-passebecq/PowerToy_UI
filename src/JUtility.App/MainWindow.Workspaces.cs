using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JUtility.App.Services;
using JUtility.Core.Actions;
using JUtility.Core.Models;
using JUtility.Core.Services;
using JUtility.Core.Workspaces;
using Microsoft.Win32;

namespace JUtility.App;

// Incremental shell layer: existing editors and their data remain the only source of truth.
public partial class MainWindow
{
    private ShellState? _sessionShell;
    private ShellStateStore? _sessionStore;
    private bool _sessionInitialized, _restoringSession, _sessionDirty;
    private readonly ComboBox _workspaceSelector = new() { MinWidth = 140, MaxWidth = 200, DisplayMemberPath = "Name", Margin = new Thickness(4) };
    private readonly WrapPanel _sessionTabs = new();
    private readonly DispatcherTimer _sessionSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private readonly CancellationTokenSource _inventoryLifetime = new();
    private StackPanel? _launchpadBody;
    private DataGrid? _inventoryGrid;
    private TextBlock? _inventoryStatus;
    private bool _inventoryScanning;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_sessionInitialized) return;
        _sessionInitialized = true;
        try
        {
            _sessionStore = new ShellStateStore(_viewModel.DataDirectory);
            _sessionShell = _sessionStore.Load();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            ShowOwnedMessage("V2 workspace state could not be loaded. Existing files are untouched; the classic shell remains usable.\n\n" + ex.Message, "V2 workspace state", MessageBoxImage.Warning);
            return;
        }

        WorkspacePanel.Items.Add(new TabItem { Header = "Launchpad", Content = CreateLaunchpad() });
        WorkspacePanel.Items.Add(new TabItem { Header = "Environment inventory", Content = CreateInventoryPage() });
        WorkspacePanel.Items.Add(new TabItem { Header = "Feature catalog", Content = CreateFeaturePage() });
        WorkspacePanel.Items.Add(new TabItem { Header = "Web", Content = CreateWebPage() });
        WorkspacePanel.Items.Add(new TabItem { Header = CredentialsHeader, Content = CreateCredentialsPage() });

        var original = (UIElement)Content;
        Content = null;
        var root = new DockPanel();
        var chrome = new StackPanel { Background = Brushes.White };
        chrome.Children.Add(CreateWorkspaceMenu());
        var toolbar = new WrapPanel { Margin = new Thickness(6, 2, 6, 2) };
        toolbar.Children.Add(new TextBlock { Text = "Workspace", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        toolbar.Children.Add(_workspaceSelector);
        toolbar.Children.Add(SessionButton("+ Workspace", NewSessionWorkspace));
        toolbar.Children.Add(SessionButton("Customize", CustomizeSessionWorkspace));
        toolbar.Children.Add(SessionButton("Save view", SaveSessionBookmark));
        toolbar.Children.Add(SessionButton("Restore view", RestoreSessionBookmark));
        toolbar.Children.Add(SessionButton("+ Quick capture", () => ShowQuickCapture()));
        toolbar.Children.Add(new TextBlock { Text = "V2 preview | on-demand only", Foreground = Brushes.DimGray, Margin = new Thickness(10, 7, 4, 4) });
        chrome.Children.Add(toolbar);
        chrome.Children.Add(new ScrollViewer { Content = _sessionTabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        DockPanel.SetDock(chrome, Dock.Top);
        root.Children.Add(chrome); root.Children.Add(original); Content = root;

        _workspaceSelector.SelectionChanged += (_, _) =>
        {
            if (_restoringSession || _workspaceSelector.SelectedItem is not WorkspaceProfile profile || _sessionShell is null) return;
            RememberSession(); _sessionShell.ActiveWorkspaceId = profile.Id;
            RestoreSession(); MarkSessionDirty();
        };
        WorkspacePanel.SelectionChanged += NativeModuleSelectionChanged;
        ShellSearchBox.TextChanged += (_, _) => MarkSessionDirty();
        RepositoryTree.SelectedItemChanged += (_, _) => MarkSessionDirty();
        root.AddHandler(Button.ClickEvent, new RoutedEventHandler((_, _) => MarkSessionDirty()), true);
        _sessionSaveTimer.Tick += (_, _) => { _sessionSaveTimer.Stop(); SaveSession(); };
        PreviewKeyDown += SessionKeyDown;
        Closing += SessionClosing;
        Closed += (_, _) => { _sessionSaveTimer.Stop(); _inventoryLifetime.Cancel(); _inventoryLifetime.Dispose(); };
        RestoreSession(); MarkSessionDirty();
    }

    private Button SessionButton(string title, Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(3), Padding = new Thickness(8, 4, 8, 4), ToolTip = title };
        AutomationProperties.SetName(button, title);
        button.Click += (_, _) => SessionAction(action);
        return button;
    }
    private void SessionAction(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or UnauthorizedAccessException or JsonException or ArgumentException or Win32Exception)
        { ShowOwnedMessage(ex.Message, "Power Ops workspace", MessageBoxImage.Warning); }
    }
    private Menu CreateWorkspaceMenu()
    {
        var menu = new Menu { Background = Brushes.White };
        MenuItem Group(string title) { var item = new MenuItem { Header = title }; menu.Items.Add(item); return item; }
        void Add(MenuItem parent, string title, Action action)
        {
            var item = new MenuItem { Header = title }; item.Click += (_, _) => SessionAction(action); parent.Items.Add(item);
        }
        var file = Group("_File");
        Add(file, "Export all content JSON...", () => ExportSessionContent(ModuleCatalog.All.Select(x => x.Id), includeShell: true));
        Add(file, "Export current module JSON...", () => ExportSessionContent([WorkspaceSessions.ActiveTab(CurrentWorkspace()).ModuleId]));
        Add(file, "Choose modules to export...", ChooseSessionExport);
        Add(file, "Export captures for AtlasNote (review)...", ExportAtlasNoteHandoff);
        file.Items.Add(new Separator());
        Add(file, "Export layout JSON...", ExportSessionLayout);
        Add(file, "Import layout JSON (preview)...", ImportSessionLayout);
        var workspace = Group("_Workspace");
        Add(workspace, "New workspace...", NewSessionWorkspace);
        Add(workspace, "Customize visible modules...", CustomizeSessionWorkspace);
        Add(workspace, "Save current view...", SaveSessionBookmark);
        Add(workspace, "Restore saved view...", RestoreSessionBookmark);
        Add(workspace, "Delete current workspace view...", DeleteSessionWorkspace);
        var tabs = Group("_Tabs");
        Add(tabs, "New tab (Ctrl+T)", () => NewSessionTab(WorkspaceSessions.ActiveTab(CurrentWorkspace()).ModuleId));
        Add(tabs, "Close tab (Ctrl+W)", CloseSessionTab);
        Add(tabs, "Reopen closed tab (Ctrl+Shift+T)", ReopenSessionTab);
        menu.Items.Add(CreateActionsMenu());
        var view = Group("_View");
        Add(view, "Show / hide quick ribbon", () => { RememberSession(); CurrentWorkspace().RibbonVisible = !CurrentWorkspace().RibbonVisible; RestoreSession(); MarkSessionDirty(); });
        Add(view, "Feature catalog", () => NavigateSession("features"));
        Add(view, "Credentials & IDs", () => NavigateSession("credentials"));
        return menu;
    }
    private WorkspaceProfile CurrentWorkspace() => WorkspaceSessions.Active(_sessionShell!);
    private void RememberSession()
    {
        if (_sessionShell is null || _restoringSession) return;
        var profile = CurrentWorkspace(); var tab = WorkspaceSessions.ActiveTab(profile);
        var module = ModuleCatalog.Get(tab.ModuleId);
        tab.Search = GetModuleSearch(module.Header); tab.Filter = GetModuleFilter(module.Header);
        tab.Families = _activeRepositoryFamilies.ToList(); tab.Providers = _activeResourceProviders.ToList(); tab.Subjects = _activeCaptureSubjects.ToList();
        profile.Layout = _viewModel.ViewMode;
    }
    private void RestoreSession()
    {
        if (_sessionShell is null) return;
        _restoringSession = true;
        try
        {
            var profile = CurrentWorkspace(); var tab = WorkspaceSessions.ActiveTab(profile); var module = ModuleCatalog.Get(tab.ModuleId);
            _moduleSearchTerms[module.Header] = tab.Search; _moduleFilters[module.Header] = tab.Filter;
            _activeRepositoryFamilies.Clear(); _activeRepositoryFamilies.UnionWith(tab.Families);
            _activeResourceProviders.Clear(); _activeResourceProviders.UnionWith(tab.Providers);
            _activeCaptureSubjects.Clear(); _activeCaptureSubjects.UnionWith(tab.Subjects);
            _viewModel.ViewMode = profile.Layout; ApplyViewMode(profile.Layout);
            SelectWorkspaceTab(module.Header);
            if (module.Id is "home" or "inventory" or "features")
            { CurrentModuleTitle.Text = module.Title; CurrentModuleSubtitle.Text = module.Purpose; }
            QuickAddButton.IsEnabled = module.Id is not ("home" or "inventory" or "features" or "web");
            if (PowerOpsShell.Children.Count > 0) PowerOpsShell.Children[0].Visibility = profile.RibbonVisible ? Visibility.Visible : Visibility.Collapsed;
            _workspaceSelector.ItemsSource = _sessionShell.Workspaces.ToArray(); _workspaceSelector.SelectedItem = profile;
            RenderSessionNavigation(); RenderSessionTabs();
            if (module.Id == "home") RefreshLaunchpad();
        }
        finally { _restoringSession = false; }
        RefreshQuickShelfForWorkspace();
        SyncEmbeddedWeb();
    }
    private void RenderSessionNavigation()
    {
        var profile = CurrentWorkspace();
        PrimaryNavPanel.Children.Clear();
        foreach (var group in ModuleCatalog.All.Where(x => profile.VisibleModules.Contains(x.Id)).GroupBy(x => x.Category))
        {
            PrimaryNavPanel.Children.Add(new TextBlock { Text = group.Key, Foreground = Brushes.DimGray, FontSize = 10, Margin = new Thickness(3, 9, 3, 3) });
            foreach (var module in group)
            {
                var button = SessionButton(module.Title, () => NavigateSession(module.Id));
                button.Tag = module.Header; button.Style = TryFindResource("PrimaryNavButtonStyle") as Style;
                PrimaryNavPanel.Children.Add(button);
            }
        }
        UpdatePrimaryNavSelection();
    }
    private void RenderSessionTabs()
    {
        _sessionTabs.Children.Clear();
        var profile = CurrentWorkspace();
        foreach (var tab in profile.Tabs)
        {
            var button = SessionButton(ModuleCatalog.Get(tab.ModuleId).Title, () =>
            { RememberSession(); profile.ActiveTabId = tab.Id; RestoreSession(); MarkSessionDirty(); });
            button.FontWeight = tab.Id == profile.ActiveTabId ? FontWeights.Bold : FontWeights.Normal;
            button.ToolTip = ModuleCatalog.Get(tab.ModuleId).Title + (tab.Search.Length == 0 ? "" : " | " + tab.Search);
            _sessionTabs.Children.Add(button);
        }
        _sessionTabs.Children.Add(SessionButton("+ (Ctrl+T)", () => SessionAction(() => NewSessionTab(WorkspaceSessions.ActiveTab(CurrentWorkspace()).ModuleId))));
        _sessionTabs.Children.Add(SessionButton("Close (Ctrl+W)", CloseSessionTab));
    }
    private void NativeModuleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sessionShell is null || _restoringSession || !ReferenceEquals(e.OriginalSource, WorkspacePanel) || WorkspacePanel.SelectedItem is not TabItem item) return;
        var module = ModuleCatalog.FromHeader(item.Header?.ToString() ?? "");
        if (module is null) return;
        var profile = CurrentWorkspace();
        if (!profile.VisibleModules.Contains(module.Id))
        {
            // Visibility is a convenience, not authorization; explicit navigation reveals the module.
            profile.VisibleModules.Add(module.Id);
        }
        var tab = WorkspaceSessions.ActiveTab(profile);
        tab.ModuleId = module.Id;
        tab.Search = GetModuleSearch(module.Header); tab.Filter = GetModuleFilter(module.Header);
        QuickAddButton.IsEnabled = module.Id is not ("home" or "inventory" or "features" or "web");
        RenderSessionNavigation(); RenderSessionTabs(); MarkSessionDirty();
        SyncEmbeddedWeb();
    }
    private void NavigateSession(string id)
    {
        if (_sessionShell is null) return;
        RememberSession(); var profile = CurrentWorkspace();
        if (!profile.VisibleModules.Contains(id)) profile.VisibleModules.Add(id);
        var tab = WorkspaceSessions.ActiveTab(profile); tab.ModuleId = id;
        tab.Search = ""; tab.Filter = "all"; tab.Families.Clear(); tab.Providers.Clear(); tab.Subjects.Clear();
        RestoreSession(); MarkSessionDirty();
    }
    private void NewSessionTab(string id)
    { RememberSession(); WorkspaceSessions.AddTab(CurrentWorkspace(), id); RestoreSession(); MarkSessionDirty(); }
    private void CloseSessionTab()
    { RememberSession(); WorkspaceSessions.CloseTab(CurrentWorkspace()); RestoreSession(); MarkSessionDirty(); }
    private void ReopenSessionTab()
    { SessionAction(() => { RememberSession(); WorkspaceSessions.ReopenTab(CurrentWorkspace()); RestoreSession(); MarkSessionDirty(); }); }
    private void MarkSessionDirty()
    {
        if (_sessionShell is null || _restoringSession) return;
        _sessionDirty = true; _sessionSaveTimer.Stop(); _sessionSaveTimer.Start();
    }
    private bool SaveSession()
    {
        if (_sessionShell is null || _sessionStore is null || !_sessionDirty) return true;
        try { RememberSession(); _sessionStore.Save(_sessionShell); _sessionDirty = false; return true; }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        { _viewModel.StatusText = "Workspace view not saved: " + ex.Message; return false; }
    }
    private void SessionClosing(object? sender, CancelEventArgs e)
    {
        if (e.Cancel || _sessionShell is null) return;
        _sessionDirty = true;
        if (SaveSession()) return;
        _suppressAutoHide = true;
        try { e.Cancel = MessageBox.Show(this, "Workspace view could not be saved. Close anyway? Business-data saving is handled separately.", "View save failed", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes; }
        finally { _suppressAutoHide = false; }
    }
    private void SessionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || _sessionShell is null || IsTextEditingControl(Keyboard.FocusedElement)) return;
        ModifierKeys keys = Keyboard.Modifiers;
        if (keys != ModifierKeys.Control && keys != (ModifierKeys.Control | ModifierKeys.Shift)) return;
        Action? action = null;
        if (e.Key == Key.T) action = keys == ModifierKeys.Control ? () => NewSessionTab(WorkspaceSessions.ActiveTab(CurrentWorkspace()).ModuleId) : ReopenSessionTab;
        if (e.Key == Key.W && keys == ModifierKeys.Control) action = CloseSessionTab;
        if (e.Key == Key.Tab)
        {
            // Same implementation as the tab.next/tab.previous quick actions.
            e.Handled = true;
            RunQuickAction(keys == ModifierKeys.Control ? QuickActionCatalog.TabNext : QuickActionCatalog.TabPrevious, ActionSurface.InAppShortcut);
            return;
        }
        if (keys == ModifierKeys.Control && (int)e.Key >= (int)Key.D1 && (int)e.Key <= (int)Key.D9)
        {
            int number = (int)e.Key - (int)Key.D1;
            action = () => { RememberSession(); var p = CurrentWorkspace(); int index = number == 8 ? p.Tabs.Count - 1 : number; if (index < p.Tabs.Count) { p.ActiveTabId = p.Tabs[index].Id; RestoreSession(); MarkSessionDirty(); } };
        }
        if (action is not null) { e.Handled = true; SessionAction(action); }
    }
    private bool SessionDialog(Window dialog)
    {
        bool previous = _suppressAutoHide; _suppressAutoHide = true;
        try { dialog.Owner = this; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; return dialog.ShowDialog() == true; }
        finally { _suppressAutoHide = previous; }
    }
    private static Window SessionDialogWindow(string title, Panel content) => new()
    { Title = title, Width = 490, Height = 560, MinWidth = 360, MinHeight = 300, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Background = Brushes.White };
    private string? AskSessionName(string title, string initial)
    {
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        var text = new TextBox { Text = initial, MaxLength = 100, Margin = new Thickness(0, 12, 0, 12) }; body.Children.Add(text);
        var dialog = SessionDialogWindow(title, body); dialog.Height = 200;
        body.Children.Add(SessionButton("Save", () => { if (!string.IsNullOrWhiteSpace(text.Text)) dialog.DialogResult = true; }));
        return SessionDialog(dialog) ? text.Text.Trim() : null;
    }
    private void NewSessionWorkspace() => SessionAction(() =>
    {
        if (_sessionShell!.Workspaces.Count >= WorkspaceSessions.MaxWorkspaces) throw new InvalidOperationException("Workspace limit reached.");
        string? name = AskSessionName("New workspace view (shares existing data)", "New workspace");
        if (name is null) return;
        RememberSession(); var profile = WorkspaceSessions.NewProfile(name);
        _sessionShell.Workspaces.Add(profile); _sessionShell.ActiveWorkspaceId = profile.Id;
        RestoreSession(); MarkSessionDirty();
    });
    private void CustomizeSessionWorkspace() => SessionAction(() =>
    {
        RememberSession(); var current = CurrentWorkspace();
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = "Visible modules in this workspace. Hiding a module does not delete its data or disable an external application.", TextWrapping = TextWrapping.Wrap });
        var name = new TextBox { Text = current.Name, MaxLength = 100, Margin = new Thickness(0, 10, 0, 10) }; body.Children.Add(name);
        var checks = new Dictionary<string, CheckBox>();
        foreach (var group in ModuleCatalog.All.GroupBy(m => m.Category))
        {
            body.Children.Add(new TextBlock { Text = group.Key, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
            foreach (var module in group)
            {
                var check = new CheckBox { Content = module.Title, IsChecked = current.VisibleModules.Contains(module.Id), IsEnabled = module.Id is not ("home" or "settings"), Margin = new Thickness(3) };
                checks.Add(module.Id, check); body.Children.Add(check);
            }
        }
        var dialog = SessionDialogWindow("Customize workspace", body);
        body.Children.Add(SessionButton("Apply", () => { if (!string.IsNullOrWhiteSpace(name.Text)) dialog.DialogResult = true; }));
        if (!SessionDialog(dialog)) return;
        current.Name = name.Text.Trim(); WorkspaceSessions.SetVisibleModules(current, checks.Where(x => x.Value.IsChecked == true).Select(x => x.Key));
        RestoreSession(); MarkSessionDirty();
    });
    private void SaveSessionBookmark() => SessionAction(() =>
    {
        string? name = AskSessionName("Save tabs, filters and layout (not a data snapshot)", CurrentWorkspace().Name + " view");
        if (name is null) return; RememberSession(); WorkspaceSessions.SaveView(_sessionShell!, name); MarkSessionDirty();
    });
    private void RestoreSessionBookmark() => SessionAction(() =>
    {
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = "Restore a saved view. Current tab/filter state will be replaced; project content is not rolled back.", TextWrapping = TextWrapping.Wrap });
        var list = new ListBox { ItemsSource = _sessionShell!.Bookmarks.ToArray(), DisplayMemberPath = "Name", MinHeight = 160, Margin = new Thickness(0, 12, 0, 12) }; body.Children.Add(list);
        var dialog = SessionDialogWindow("Saved views", body);
        body.Children.Add(SessionButton("Restore selected", () => { if (list.SelectedItem is WorkspaceBookmark) dialog.DialogResult = true; }));
        body.Children.Add(SessionButton("Remove selected bookmark", () =>
        { if (list.SelectedItem is WorkspaceBookmark b) { _sessionShell.Bookmarks.Remove(b); list.ItemsSource = _sessionShell.Bookmarks.ToArray(); MarkSessionDirty(); } }));
        if (!SessionDialog(dialog) || list.SelectedItem is not WorkspaceBookmark selected) return;
        RememberSession(); _sessionDirty = true;
        if (!SaveSession()) throw new IOException("Save the current view successfully before restoring another.");
        WorkspaceSessions.RestoreView(_sessionShell, selected.Id); RestoreSession(); MarkSessionDirty();
    });
    private void DeleteSessionWorkspace()
    {
        if (_sessionShell!.Workspaces.Count <= 1) return;
        string? confirm = AskSessionName("Type DELETE to remove this workspace VIEW only", "");
        if (confirm != "DELETE") return;
        _sessionShell.Workspaces.Remove(CurrentWorkspace()); _sessionShell.ActiveWorkspaceId = _sessionShell.Workspaces[0].Id;
        RestoreSession(); MarkSessionDirty();
    }
    private void ChooseSessionExport()
    {
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = "Choose content sections. User text and URLs may contain sensitive information. JSON does not include media files.", TextWrapping = TextWrapping.Wrap });
        var checks = ModuleCatalog.All.ToDictionary(m => m.Id, m => new CheckBox { Content = m.Title, IsChecked = CurrentWorkspace().VisibleModules.Contains(m.Id), Margin = new Thickness(3) });
        foreach (var check in checks.Values) body.Children.Add(check);
        var dialog = SessionDialogWindow("Export selected modules", body);
        body.Children.Add(SessionButton("Continue", () => dialog.DialogResult = true));
        if (SessionDialog(dialog)) ExportSessionContent(checks.Where(x => x.Value.IsChecked == true).Select(x => x.Key));
    }
    private void ExportSessionContent(IEnumerable<string> ids, bool includeShell = false) => SessionAction(() =>
    {
        RememberSession();
        if (!SafeSave()) return;
        var snapshot = new WorkspaceStore(_viewModel.DataDirectory).Load();
        EnsureCredentials(); // Secret values are never in the catalog; private IDs export without their value.
        string json = PortableExport.Create(snapshot, _sessionShell!, ids, includeShell: includeShell, credentials: _credentials);
        ShowOwnedMessage("Review before sharing: selected text and URLs may contain private information. Local launcher commands/paths are omitted. Media bytes are not included. This review/export format is NOT a recovery backup.", "Content export", MessageBoxImage.Information);
        WriteSessionExport(json, "PowerOps-content.json");
    });
    private void ExportSessionLayout()
    { RememberSession(); WriteSessionExport(JsonSerializer.Serialize(_sessionShell, WorkspaceSessions.Json), "PowerOps-layout.json"); }
    private bool WriteSessionExport(string json, string fileName)
    {
        bool previous = _suppressAutoHide; _suppressAutoHide = true;
        try
        {
            var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = fileName, DefaultExt = ".json", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return false;
            string destination = Path.GetFullPath(dialog.FileName);
            string root = Path.GetFullPath(_viewModel.DataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Save exports outside the active data directory to protect managed files.");
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, json); File.Move(temporary, destination, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            _viewModel.StatusText = "Exported " + Path.GetFileName(destination);
            return true;
        }
        finally { _suppressAutoHide = previous; }
    }
    private void ImportSessionLayout()
    {
        bool previous = _suppressAutoHide; _suppressAutoHide = true;
        try
        {
            var dialog = new OpenFileDialog { Filter = "Power Ops layout JSON (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            var candidate = ShellStateStore.Read(dialog.FileName);
            string summary = string.Join("\n", candidate.Workspaces.Select(x => $"{x.Name}: {x.Tabs.Count} tabs, {x.VisibleModules.Count} visible modules"));
            if (MessageBox.Show(this, "Replace UI workspace views with:\n\n" + summary + "\n\nBusiness data and external applications are not changed. Existing valid shell state is backed up.", "Preview layout import", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _sessionStore!.Save(candidate); _sessionShell = candidate;
            RestoreSession(); _sessionDirty = false;
        }
        finally { _suppressAutoHide = previous; }
    }
    private UIElement CreateLaunchpad()
    {
        _launchpadBody = new StackPanel { Margin = new Thickness(18) };
        return new ScrollViewer { Content = _launchpadBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private void RefreshLaunchpad()
    {
        if (_launchpadBody is null) return;
        _launchpadBody.Children.Clear();
        RenderReportCards(_launchpadBody);
        _launchpadBody.Children.Add(new TextBlock { Text = "Your websites & project destinations", FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        _launchpadBody.Children.Add(new TextBlock { Text = "Uses existing Repository, Portal and Resource data. Links are not live health checks. No embedded browser is running.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 8) });
        _launchpadBody.Children.Add(SessionButton("Refresh saved links", RefreshLaunchpad));
        var destinations = _viewModel.Projects.Where(p => !p.IsArchived && !string.IsNullOrWhiteSpace(p.SiteUrl)).Select(p => (p.Name, Group: p.Category, Url: p.SiteUrl))
            .Concat(_viewModel.Portals.Select(p => (p.Name, Group: p.Category, Url: p.MainUrl)))
            .Concat(_viewModel.Resources.Select(r => (r.Name, Group: r.Group, Url: r.Url)))
            .Where(x => Uri.TryCreate(x.Url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "https" or "http")
            .DistinctBy(x => x.Url, StringComparer.Ordinal).Take(150).GroupBy(x => x.Group);
        foreach (var group in destinations)
        {
            _launchpadBody.Children.Add(new TextBlock { Text = group.Key, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6) });
            var cards = new WrapPanel(); _launchpadBody.Children.Add(cards);
            foreach (var link in group)
            {
                var body = new StackPanel { Width = 260, Margin = new Thickness(8) };
                body.Children.Add(new TextBlock { Text = link.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                body.Children.Add(new TextBlock { Text = link.Url, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, MaxHeight = 45 });
                body.Children.Add(SessionButton("Open in browser", () => SessionAction(() => Process.Start(new ProcessStartInfo(link.Url) { UseShellExecute = true }))));
                cards.Children.Add(new Border { Child = body, Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(4) });
            }
        }
    }
    private UIElement CreateInventoryPage()
    {
        var body = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Environment inventory", FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Local PATH locations and executable file metadata only. No programs are executed. Not a complete installed-app, Conda-environment or plugin inventory. Runtime versions and available updates require later adapters.", TextWrapping = TextWrapping.Wrap });
        var scan = SessionButton("Scan local PATH (on demand)", () => { });
        scan.Click += async (_, _) =>
        {
            if (_inventoryScanning) return;
            _inventoryScanning = true; scan.IsEnabled = false;
            try
            {
                CancellationToken token = _inventoryLifetime.Token;
                var rows = await Task.Run(() => EnvironmentPathInventory.Scan(token), token);
                if (_inventoryLifetime.IsCancellationRequested) return;
                _inventoryGrid!.ItemsSource = rows;
                _inventoryStatus!.Text = $"Observed {DateTime.Now:HH:mm:ss} | {rows.Count} rows | up to 80 local PATH directories | file version is not necessarily runtime version";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!_inventoryLifetime.IsCancellationRequested) _inventoryStatus!.Text = "Scan failed: " + ex.Message; }
            finally { _inventoryScanning = false; scan.IsEnabled = true; }
        };
        header.Children.Add(scan);
        _inventoryStatus = new TextBlock { Text = "Not scanned. No background scan is scheduled.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) }; header.Children.Add(_inventoryStatus);
        _inventoryGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, EnableRowVirtualization = true };
        body.Children.Add(_inventoryGrid); return body;
    }
    private static UIElement CreateFeaturePage()
    {
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = "Feature catalog & boundaries", FontSize = 24, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "V2 foundation preview. Module visibility is per workspace; underlying data remains shared. Existing V1 editors are still eagerly constructed. No live monitoring or cloud sync is claimed.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        foreach (var group in ModuleCatalog.All.GroupBy(x => x.Category))
        {
            body.Children.Add(new TextBlock { Text = group.Key, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            foreach (var module in group) body.Children.Add(new TextBlock { Text = module.Title + " - " + module.Purpose, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
        }
        body.Children.Add(new TextBlock { Text = "Planned / NOT implemented in this preview", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) });
        body.Children.Add(new TextBlock { Text = "Git/PR/CI activity; Codex/Claude completion adapters; full runtimes/environments/extensions inventory; AtlasNote task sync; Mongoku summaries; cloud free-tier adapters; CPU/GPU/RAM/battery sampling; screen-time; RSS/market feeds; monitor docking and mirrored companions; protected maintenance recipes. See docs/v2/ARCHITECTURE.md.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Never include secrets in exported JSON. No automatic updates, Git pushes, cleanup/deletion, power changes or credential collection: secrets are only ever typed in explicitly under Credentials & IDs and stored in Windows Credential Manager. External tools keep ownership of their data.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        return new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
