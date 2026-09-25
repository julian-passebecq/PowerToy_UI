using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JUtility.Core.Actions;

namespace JUtility.App;

// V2.1 Quick Ring: transient radial menu at the pointer over the same action dispatcher.
public partial class MainWindow
{
    private QuickRingWindow? _ring;

    private string? RingUnavailable() => _quickActionSettings is null
        ? "Quick action settings could not be read, so the Quick Ring is off. The file was not changed."
        : null;

    private void ToggleQuickRing() => ShowQuickRing(null);

    /// <param name="groupActionId">Open directly on this sub-ring (a "group:" action bound to a shortcut or the Shelf).</param>
    private void ShowQuickRing(string? groupActionId)
    {
        if (_ring?.IsVisible == true)
        {
            _ring.Dismiss();
            return;
        }

        if (_quickActionSettings is null) return;
        if (_ring is null)
        {
            _ring = new QuickRingWindow();
            _ring.ActionInvoked += (_, id) => RunQuickAction(id ?? QuickActionCatalog.AppOpen, ActionSurface.QuickRing);
            Closed += (_, _) => _ring.Close();
        }

        // Availability is probed now, once per slot (and once per sub-ring slot when it opens): showing the ring starts nothing else.
        RefreshToolActions();
        QuickActionSettings settings = _quickActionSettings;
        string? Unavailable(string id) => _quickActions.UnavailableReason(id);
        string? Gesture(string id) => string.Join(", ", _hotkeys.Registered.Where(x => x.ActionId == id).Select(x => x.Gesture.ToString()));
        IReadOnlyList<QuickSurfaceItem> items = QuickRingModel.Build(
            settings, _sessionShell?.ActiveWorkspaceId ?? Guid.Empty, Unavailable, Gesture, _quickActions.Definition);
        QuickSurfaceItem? start = groupActionId is null
            ? null
            : QuickSurfaceModel.Build(settings, [groupActionId], Unavailable, Gesture, _quickActions.Definition)[0];
        _ring.ShowAtPointer(items, id => QuickRingModel.BuildGroup(settings, id, Unavailable, Gesture, _quickActions.Definition), start);
    }

    /// <summary>Ring groups as dynamic actions: from a shortcut or the Shelf, a group opens the Quick Ring on that sub-ring.</summary>
    private void RegisterRingGroups() => _quickActions.ReplaceDynamic(
        QuickRingGroups.Prefix,
        QuickRingGroups.Definitions(_quickActionSettings).Select(definition =>
        {
            string id = definition.Id;
            return (definition, new QuickActionHandler(() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ShowQuickRing(id)), RingUnavailable));
        }).ToList());

    /// <summary>
    /// Tool Launcher entries as "tool:" actions. Tools are business data that can change at any time, so this runs on
    /// demand (before a surface or editor is shown, and before a tool action runs), never on a timer.
    /// </summary>
    private void RefreshToolActions() => _quickActions.ReplaceDynamic(
        QuickToolActions.Prefix,
        _viewModel.Tools.Where(tool => !string.IsNullOrWhiteSpace(tool.Command)).Select(tool =>
        {
            Guid toolId = tool.Id;
            return (QuickToolActions.Definition(tool.Id, tool.Name, tool.Command), new QuickActionHandler(
                () => LaunchTool(_viewModel.Tools.First(x => x.Id == toolId)),
                () => _viewModel.Tools.Any(x => x.Id == toolId) ? null : "This tool was removed from Tool Launcher."));
        }).ToList());

    private void CustomizeQuickRing() => SessionAction(() =>
    {
        if (_quickActionSettings is null)
        {
            ShowOwnedMessage(RingUnavailable()!, "Quick Ring", MessageBoxImage.Warning);
            return;
        }

        BringToFront();
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = "The Quick Ring opens around the pointer (bind \"Quick Ring\" to a global shortcut, or to a mouse button via Logi Options+). "
                + "Up to 8 slots, numbered clockwise from the top: press 1-8, use the arrow keys and Enter, or click. "
                + "The centre always opens Power Ops. Esc or clicking outside closes it. "
                + "A slot marked › (Folders, Apps...) opens a second ring; its centre or Esc goes back.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        LayoutEditorState layout = AddLayoutEditor(body, ActionSurface.QuickRing, _quickActionSettings);

        Window dialog = SessionDialogWindow("Customize Quick Ring", body);
        dialog.Height = 680;
        Action? next = null;
        var levels = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        levels.Children.Add(SessionButton("Sub-rings (Folders, Apps...)...", () =>
        {
            next = ManageRingGroups;
            dialog.DialogResult = false;
        }));
        levels.Children.Add(SessionButton("Use suggested layout", () =>
        {
            if (MessageBox.Show(dialog,
                    "Replace the default ring with: Screenshot, Whole screen, Quick Capture, Clipboard, Folders ›, Apps ›, Resume workspace?\n\n"
                    + "Folders › gets Downloads, Desktop, Explorer folder and File tray; Apps › gets the terminal and your web apps. "
                    + "Other sub-rings and per-workspace rings are kept.",
                    "Suggested Quick Ring", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            string? failure = TryUpdateQuickActionSettings(QuickRingGroups.ApplySuggested);
            if (failure is not null) { layout.Error.Text = failure; return; }
            next = CustomizeQuickRing;
            dialog.DialogResult = false;
        }));
        body.Children.Add(levels);
        body.Children.Add(SessionButton("Save", () =>
        {
            string? failure = TryUpdateQuickActionSettings(s => ApplyLayoutEditor(s, ActionSurface.QuickRing, layout));
            if (failure is null) dialog.DialogResult = true;
            else layout.Error.Text = failure;
        }));

        if (SessionDialog(dialog))
        {
            _viewModel.StatusText = "Quick Ring saved";
        }
        else if (next is not null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, next);
        }
    });
}
