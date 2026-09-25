using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JUtility.App.Services;
using JUtility.Core.Actions;
using JUtility.Core.Models;
using JUtility.Core.Workspaces;

namespace JUtility.App;

// V2.1 Quick Actions wiring: the ONLY place catalog IDs get an implementation. Menus, in-app
// shortcuts and global hotkeys (and later the Ring, Shelf and MX Master mappings) all invoke these.
public partial class MainWindow
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    private readonly QuickActionDispatcher _quickActions = new();
    private readonly GlobalHotkeyService _hotkeys = new();
    private QuickActionSettingsStore? _quickActionStore;
    // Null when quick-actions.json could not be read: global shortcuts stay off and the file is left untouched.
    private QuickActionSettings? _quickActionSettings;
    private string? _quickActionLoadError;
    private string _hotkeyStatus = "Global shortcuts are off.";
    private MenuItem? _actionsMenu;

    private void InitializeQuickActions()
    {
        void Register(string id, Action execute, Func<string?>? unavailable = null) =>
            _quickActions.Register(id, new QuickActionHandler(execute, unavailable));

        Register(QuickActionCatalog.AppToggle, ToggleFromQuickAction);
        Register(QuickActionCatalog.AppOpen, BringToFront);
        Register(QuickActionCatalog.CaptureRegion, () => Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true }));
        // "+ -> type/paste -> Save" in a small window; the full Capture editor stays one click away in Power Ops.
        Register(QuickActionCatalog.CaptureQuick, ShowQuickCapture);
        Register(QuickActionCatalog.ClipboardOpen, () => { BringToFront(); ShowModule("clipboard"); });
        Register(
            QuickActionCatalog.FolderDownloads,
            () => OpenExplorerPath(DownloadsFolder(), "Downloads"),
            () => Directory.Exists(DownloadsFolder()) ? null : "The Windows Downloads folder was not found.");
        Register(
            QuickActionCatalog.FolderExplorer,
            OpenPinnedExplorerFolder,
            () => _viewModel.ExplorerFolders.FirstOrDefault(item => item.IsPinned) is { } pinned
                && !string.IsNullOrWhiteSpace(pinned.Path) && !Directory.Exists(pinned.Path)
                    ? $"Pinned folder not found: {pinned.Path}"
                    : null);
        Register(
            QuickActionCatalog.TerminalOpen,
            () => LaunchTool(ConfiguredTerminal()!),
            () => ConfiguredTerminal() is null ? "Add a terminal (for example Windows Terminal with command wt) in Tool Launcher first." : null);
        Register(QuickActionCatalog.WorkspaceResume, () => { BringToFront(); if (_sessionShell is not null) RestoreSession(); });
        Register(QuickActionCatalog.WorkspaceNext, () => CycleSessionWorkspace(1), WorkspaceCycleUnavailable);
        Register(QuickActionCatalog.WorkspacePrevious, () => CycleSessionWorkspace(-1), WorkspaceCycleUnavailable);
        Register(QuickActionCatalog.TabNext, () => CycleSessionTab(1), TabCycleUnavailable);
        Register(QuickActionCatalog.TabPrevious, () => CycleSessionTab(-1), TabCycleUnavailable);
        Register(QuickActionCatalog.ShelfToggle, ToggleQuickShelf, ShelfUnavailable);
        Register(QuickActionCatalog.RingShow, ToggleQuickRing, RingUnavailable);
        InitializeFileTray(Register);

        // Leave the WM_HOTKEY callback before running: actions may open windows or message boxes.
        _hotkeys.Pressed += (_, hotkey) => Dispatcher.BeginInvoke(
            DispatcherPriority.Send,
            new Action(() => RunQuickAction(hotkey.ActionId, ActionSurface.GlobalShortcut)));
        Closed += (_, _) => _hotkeys.Dispose();
    }

    private QuickActionResult RunQuickAction(string actionId, ActionSurface surface)
    {
        QuickActionResult result = _quickActions.Invoke(actionId, surface);
        if (result.Succeeded)
        {
            return result;
        }

        _viewModel.StatusText = result.Message;
        if (surface != ActionSurface.InAppShortcut)
        {
            // Out-of-app callers cannot see the status bar, so a refusal must be shown, never swallowed.
            BringToFront();
            ShowOwnedMessage(result.Message, "Power Ops quick action", MessageBoxImage.Information);
        }

        return result;
    }

    private void ToggleFromQuickAction()
    {
        // WPF IsActive can stay true while Windows kept another app in the foreground (for example
        // after a background launch), so ask Windows which window the user is actually looking at.
        bool inFront = IsVisible
            && WindowState != WindowState.Minimized
            && GetForegroundWindow() == new WindowInteropHelper(this).Handle;
        if (!inFront)
        {
            BringToFront();
            return;
        }

        CaptureCurrentWindowPlacement();
        if (!SafeSave(showError: true))
        {
            return;
        }

        if (_viewModel.WindowBehavior == WindowBehaviorMode.Summon)
        {
            Hide();
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void BringToFront()
    {
        if (_viewModel.WindowBehavior == WindowBehaviorMode.Summon)
        {
            ShowSummoned();
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>Activates an existing tab for the module, otherwise retargets the current tab.</summary>
    private void ShowModule(string moduleId)
    {
        if (_sessionShell is null)
        {
            SelectWorkspaceTab(ModuleCatalog.Get(moduleId).Header);
            return;
        }

        RememberSession();
        WorkspaceProfile profile = CurrentWorkspace();
        SessionTab? existing = profile.Tabs.FirstOrDefault(tab => tab.ModuleId == moduleId);
        if (existing is null)
        {
            NavigateSession(moduleId);
            return;
        }

        profile.ActiveTabId = existing.Id;
        RestoreSession();
        MarkSessionDirty();
    }

    private void CycleSessionTab(int delta)
    {
        RememberSession();
        if (WorkspaceSessions.CycleTab(CurrentWorkspace(), delta))
        {
            RestoreSession();
            MarkSessionDirty();
        }
    }

    private void CycleSessionWorkspace(int delta)
    {
        BringToFront();
        RememberSession();
        if (WorkspaceSessions.CycleWorkspace(_sessionShell!, delta))
        {
            RestoreSession();
            MarkSessionDirty();
            _viewModel.StatusText = $"Workspace: {CurrentWorkspace().Name}";
        }
    }

    private string? TabCycleUnavailable() =>
        _sessionShell is null ? "Workspace tabs are not available (V2 workspace state did not load)."
        : CurrentWorkspace().Tabs.Count < 2 ? "Only one tab is open."
        : null;

    private string? WorkspaceCycleUnavailable() =>
        _sessionShell is null ? "Workspace views are not available (V2 workspace state did not load)."
        : _sessionShell.Workspaces.Count < 2 ? "Only one workspace view exists."
        : null;

    private ToolLauncherEntry? ConfiguredTerminal() =>
        QuickActionTargets.PickTerminal(_viewModel.Tools, tool => tool.Name, tool => tool.Command);

    private static string DownloadsFolder()
    {
        try
        {
            return SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    private void LoadQuickActionSettings()
    {
        try
        {
            _quickActionStore = new QuickActionSettingsStore(_viewModel.DataDirectory);
            _quickActionSettings = _quickActionStore.Load();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            _quickActionSettings = null;
            _quickActionLoadError = ex.Message;
            _hotkeyStatus = "Global shortcuts are off because quick-actions.json could not be read. The file was not changed.";
            ShowOwnedMessage(
                "Quick action settings could not be loaded, so global shortcuts stay off. The existing file was left untouched.\n\n" + ex.Message,
                "Power Ops quick actions",
                MessageBoxImage.Warning);
            return;
        }

        RegisterWebApps();
        IReadOnlyList<string> failures = ApplyGlobalHotkeys();
        ShowQuickShelfAtStartup();
        if (failures.Count > 0)
        {
            ShowOwnedMessage(_hotkeyStatus, "Power Ops global shortcuts", MessageBoxImage.Warning);
        }
    }

    private IReadOnlyList<string> ApplyGlobalHotkeys()
    {
        if (_quickActionSettings is null)
        {
            _hotkeys.UnregisterAll();
            return [];
        }

        IReadOnlyList<PlannedHotkey> plan = QuickActionHotkeys.Plan(_quickActionSettings);
        IReadOnlyList<string> failures = _hotkeys.Apply(plan);
        _hotkeyStatus = !_quickActionSettings.GlobalShortcutsEnabled
            ? "Global shortcuts are off."
            : $"{_hotkeys.Registered.Count} of {plan.Count} global shortcut(s) active."
                + (failures.Count == 0 ? string.Empty : "\n\n" + string.Join("\n", failures));
        if (failures.Count > 0)
        {
            _viewModel.StatusText = "Some global shortcuts could not be registered";
        }

        RefreshActionsMenu();
        if (_shelf?.IsVisible == true) RenderQuickShelf();
        return failures;
    }

    private MenuItem CreateActionsMenu()
    {
        _actionsMenu = new MenuItem { Header = "_Actions" };
        RefreshActionsMenu();
        return _actionsMenu;
    }

    private void RefreshActionsMenu()
    {
        if (_actionsMenu is null)
        {
            return;
        }

        _actionsMenu.Items.Clear();
        bool webSeparator = false;
        // File tray actions are grouped in a submenu; from a menu they act on the newest tray file.
        var tray = new MenuItem { Header = "File tray" };
        foreach (QuickActionDefinition action in _quickActions.Definitions.Where(x => _quickActions.IsRegistered(x.Id)))
        {
            if (!webSeparator && QuickWebApps.IsWebActionId(action.Id))
            {
                webSeparator = true;
                _actionsMenu.Items.Add(new Separator());
            }

            string gestures = string.Join(", ", _hotkeys.Registered.Where(x => x.ActionId == action.Id).Select(x => x.Gesture.ToString()));
            var item = new MenuItem
            {
                Header = action.Label,
                ToolTip = action.Description,
                InputGestureText = gestures.Length > 0 ? gestures : action.InAppShortcut ?? string.Empty,
            };
            string id = action.Id;
            item.Click += (_, _) => RunQuickAction(id, ActionSurface.FullUi);
            if (action.Category == "File tray")
            {
                if (tray.Items.Count == 0) _actionsMenu.Items.Add(tray);
                tray.Items.Add(item);
            }
            else
            {
                _actionsMenu.Items.Add(item);
            }
        }

        _actionsMenu.Items.Add(new Separator());
        var interaction = new MenuItem { Header = "Interaction settings..." };
        interaction.Click += (_, _) => EditInteraction();
        _actionsMenu.Items.Add(interaction);
        var reportCards = new MenuItem { Header = "Mongoku report cards..." };
        reportCards.Click += (_, _) => ManageReportCards();
        _actionsMenu.Items.Add(reportCards);
        var webApps = new MenuItem { Header = "Web apps..." };
        webApps.Click += (_, _) => ManageWebApps();
        _actionsMenu.Items.Add(webApps);
        var shelf = new MenuItem { Header = "Customize Quick Shelf..." };
        shelf.Click += (_, _) => CustomizeQuickShelf();
        _actionsMenu.Items.Add(shelf);
        var ring = new MenuItem { Header = "Customize Quick Ring..." };
        ring.Click += (_, _) => CustomizeQuickRing();
        _actionsMenu.Items.Add(ring);
        var settings = new MenuItem { Header = "Global shortcuts..." };
        settings.Click += (_, _) => EditGlobalShortcuts();
        _actionsMenu.Items.Add(settings);
    }

    private void EditGlobalShortcuts() => SessionAction(() =>
    {
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = "Global shortcuts run a Power Ops action while another application is focused. They are off by default. "
                + "Power Ops asks Windows to reserve only these exact key combinations; it does not install a keyboard hook. "
                + "Tab switching stays in-app only (Ctrl+Tab).",
            TextWrapping = TextWrapping.Wrap,
        });
        var status = new TextBlock { Text = _hotkeyStatus, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 6) };
        body.Children.Add(status);

        if (_quickActionSettings is null || _quickActionStore is null)
        {
            body.Children.Add(new TextBlock { Text = _quickActionLoadError ?? "Quick action settings are unavailable.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick });
            SessionDialog(SessionDialogWindow("Global shortcuts", body));
            return;
        }

        QuickActionSettings settings = _quickActionSettings;
        QuickActionDefinition[] assignable = _quickActions.Definitions
            .Where(x => x.GlobalAllowed && x.Risk != ActionRisk.Destructive && _quickActions.IsRegistered(x.Id))
            .ToArray();
        var enabled = new CheckBox { Content = "Enable global shortcuts", IsChecked = settings.GlobalShortcutsEnabled, Margin = new Thickness(0, 6, 0, 8) };
        body.Children.Add(enabled);

        var rows = new StackPanel();
        var editors = new List<(TextBox Gesture, ComboBox Action, UIElement Row)>();
        void AddRow(string gesture, string actionId)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var gestureBox = new TextBox { Text = gesture, MaxLength = 64, Margin = new Thickness(0, 0, 6, 0), ToolTip = "For example Ctrl+Alt+Space" };
            AutomationProperties.SetName(gestureBox, "Shortcut keys");
            var actionBox = new ComboBox { ItemsSource = assignable, DisplayMemberPath = nameof(QuickActionDefinition.Label), SelectedValuePath = nameof(QuickActionDefinition.Id), SelectedValue = actionId };
            AutomationProperties.SetName(actionBox, "Action");
            var remove = new Button { Content = "Remove", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2) };
            Grid.SetColumn(actionBox, 1);
            Grid.SetColumn(remove, 2);
            grid.Children.Add(gestureBox);
            grid.Children.Add(actionBox);
            grid.Children.Add(remove);
            var entry = (gestureBox, actionBox, (UIElement)grid);
            editors.Add(entry);
            rows.Children.Add(grid);
            remove.Click += (_, _) => { editors.Remove(entry); rows.Children.Remove(grid); };
        }

        foreach (ShortcutBinding binding in settings.GlobalShortcuts)
        {
            AddRow(binding.Gesture, binding.ActionId);
        }

        body.Children.Add(rows);
        body.Children.Add(SessionButton("+ Add shortcut", () =>
        {
            if (editors.Count < QuickActionLayouts.MaxGlobalShortcuts) AddRow("Ctrl+Alt+", QuickActionCatalog.AppOpen);
        }));
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(error);

        Window dialog = SessionDialogWindow("Global shortcuts", body);
        body.Children.Add(SessionButton("Save and apply", () =>
        {
            bool previousEnabled = settings.GlobalShortcutsEnabled;
            List<ShortcutBinding> previousBindings = settings.GlobalShortcuts;
            try
            {
                settings.GlobalShortcutsEnabled = enabled.IsChecked == true;
                settings.GlobalShortcuts = editors
                    .Select(x => new ShortcutBinding { Gesture = HotkeyGesture.Parse(x.Gesture.Text).ToString(), ActionId = x.Action.SelectedValue as string ?? string.Empty })
                    .ToList();
                _quickActionStore.Save(settings);
                dialog.DialogResult = true;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                settings.GlobalShortcutsEnabled = previousEnabled;
                settings.GlobalShortcuts = previousBindings;
                error.Text = ex.Message;
            }
        }));

        if (!SessionDialog(dialog))
        {
            return;
        }

        IReadOnlyList<string> failures = ApplyGlobalHotkeys();
        _viewModel.StatusText = failures.Count == 0 ? "Global shortcuts saved" : "Global shortcuts saved; some could not be registered";
        ShowOwnedMessage(_hotkeyStatus, "Power Ops global shortcuts", failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    });

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern string SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folderId, uint flags, IntPtr token);
}
