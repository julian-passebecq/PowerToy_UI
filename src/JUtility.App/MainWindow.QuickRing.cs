using System.Windows;
using System.Windows.Controls;
using JUtility.Core.Actions;

namespace JUtility.App;

// V2.1 Quick Ring: transient radial menu at the pointer over the same action dispatcher.
public partial class MainWindow
{
    private QuickRingWindow? _ring;

    private string? RingUnavailable() => _quickActionSettings is null
        ? "Quick action settings could not be read, so the Quick Ring is off. The file was not changed."
        : null;

    private void ToggleQuickRing()
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

        // Availability is probed now, once per slot: showing the ring starts nothing else.
        IReadOnlyList<QuickSurfaceItem> items = QuickRingModel.Build(
            _quickActionSettings,
            _sessionShell?.ActiveWorkspaceId ?? Guid.Empty,
            id => _quickActions.UnavailableReason(id),
            id => string.Join(", ", _hotkeys.Registered.Where(x => x.ActionId == id).Select(x => x.Gesture.ToString())));
        _ring.ShowAtPointer(items);
    }

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
                + "The centre always opens Power Ops. Esc or clicking outside closes it.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        LayoutEditorState layout = AddLayoutEditor(body, ActionSurface.QuickRing, _quickActionSettings);

        Window dialog = SessionDialogWindow("Customize Quick Ring", body);
        dialog.Height = 620;
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
    });
}
