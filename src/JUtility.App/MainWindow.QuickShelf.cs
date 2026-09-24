using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JUtility.Core.Actions;
using JUtility.Core.Workspaces;

namespace JUtility.App;

// V2.1 Quick Shelf: a compact strip over the same action dispatcher. Layout is per workspace view.
public partial class MainWindow
{
    private QuickShelfWindow? _shelf;
    private Guid _shelfWorkspaceId;

    private string? ShelfUnavailable() => _quickActionSettings is null
        ? "Quick action settings could not be read, so the Quick Shelf is off. The file was not changed."
        : null;

    private void ToggleQuickShelf()
    {
        if (_shelf?.IsVisible == true)
        {
            _shelf.HideRestoringFocus();
            return;
        }

        ShowQuickShelf(forKeyboard: true);
    }

    private void ShowQuickShelf(bool forKeyboard)
    {
        if (_quickActionSettings is null)
        {
            return;
        }

        bool firstShow = _shelf is null;
        _shelf ??= CreateQuickShelf();
        RenderQuickShelf();
        if (!_shelf.IsVisible)
        {
            PositionQuickShelf(firstShow);
        }

        if (forKeyboard) _shelf.ShowForKeyboard();
        else _shelf.ShowPassive();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_quickActionSettings?.ShelfLeft is null && _shelf.IsVisible)
            {
                // Default placement: top centre of the primary work area, once the real width is known.
                Rect work = SystemParameters.WorkArea;
                _shelf.Left = work.Left + Math.Max(0, (work.Width - _shelf.ActualWidth) / 2);
                _shelf.Top = work.Top + 8;
            }

            _shelf.EnsureOnScreen();
        }));
    }

    private QuickShelfWindow CreateQuickShelf()
    {
        var shelf = new QuickShelfWindow();
        shelf.ActionInvoked += (_, id) => RunQuickAction(id, ActionSurface.QuickShelf);
        shelf.Hovered += (_, _) => RenderQuickShelf();
        shelf.MoveCompleted += (_, _) => UpdateQuickActionSettings(s => { s.ShelfLeft = shelf.Left; s.ShelfTop = shelf.Top; }, quiet: true);

        var menu = new ContextMenu();
        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Customize Quick Shelf...", CustomizeQuickShelf);
        Add("Open Power Ops", () => RunQuickAction(QuickActionCatalog.AppOpen, ActionSurface.QuickShelf));
        menu.Items.Add(new Separator());
        Add("Hide Quick Shelf", shelf.HideRestoringFocus);
        shelf.AttachMenu(menu);
        Closed += (_, _) => shelf.Close();
        return shelf;
    }

    private void PositionQuickShelf(bool firstShow)
    {
        if (_shelf is null || !firstShow || _quickActionSettings is null) return;
        if (_quickActionSettings.ShelfLeft is double left && _quickActionSettings.ShelfTop is double top)
        {
            _shelf.Left = left;
            _shelf.Top = top;
        }
        else
        {
            // Provisional until the Loaded pass centres it with the measured width.
            _shelf.Left = SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2;
            _shelf.Top = SystemParameters.WorkArea.Top + 8;
        }
    }

    private void RenderQuickShelf()
    {
        if (_shelf is null || _quickActionSettings is null) return;
        _shelfWorkspaceId = _sessionShell?.ActiveWorkspaceId ?? Guid.Empty;
        IReadOnlyList<QuickSurfaceItem> items = QuickShelfModel.Build(
            _quickActionSettings,
            _shelfWorkspaceId,
            id => _quickActions.UnavailableReason(id),
            id => string.Join(", ", _hotkeys.Registered.Where(x => x.ActionId == id).Select(x => x.Gesture.ToString())));
        _shelf.Render(items, _quickActionSettings.ShelfOrientation, _quickActionSettings.ShelfAlwaysOnTop, _quickActionSettings.ShelfAutoHide);
    }

    /// <summary>Called after workspace/tab restores; re-renders only when the workspace view changed.</summary>
    private void RefreshQuickShelfForWorkspace()
    {
        if (_shelf?.IsVisible == true && (_sessionShell?.ActiveWorkspaceId ?? Guid.Empty) != _shelfWorkspaceId)
        {
            RenderQuickShelf();
        }
    }

    private void ShowQuickShelfAtStartup()
    {
        if (_quickActionSettings is not null && QuickShelfModel.ShowAtStartup(_quickActionSettings))
        {
            ShowQuickShelf(forKeyboard: false);
        }
    }

    /// <summary>Edits a copy, validates and saves it, and only then replaces the live settings.</summary>
    private bool UpdateQuickActionSettings(Action<QuickActionSettings> change, bool quiet = false)
    {
        string? error = TryUpdateQuickActionSettings(change);
        if (error is null) return true;
        _viewModel.StatusText = "Quick action settings not saved: " + error;
        if (!quiet) ShowOwnedMessage(error, "Power Ops quick actions", MessageBoxImage.Warning);
        return false;
    }

    /// <summary>Null on success; otherwise the reason nothing was changed.</summary>
    private string? TryUpdateQuickActionSettings(Action<QuickActionSettings> change)
    {
        if (_quickActionSettings is null || _quickActionStore is null) return ShelfUnavailable() ?? "Quick action settings are unavailable.";
        QuickActionSettings candidate = QuickActionSettingsStore.Copy(_quickActionSettings);
        try
        {
            change(candidate);
            _quickActionStore.Save(candidate);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return ex.Message;
        }

        _quickActionSettings = candidate;
        RegisterWebApps();
        if (_shelf?.IsVisible == true) RenderQuickShelf();
        return null;
    }

    private void CustomizeQuickShelf() => SessionAction(() =>
    {
        if (_quickActionSettings is null)
        {
            ShowOwnedMessage(ShelfUnavailable()!, "Quick Shelf", MessageBoxImage.Warning);
            return;
        }

        BringToFront();
        QuickActionSettings settings = _quickActionSettings;
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = "The Quick Shelf is a small strip of action buttons. It uses the same actions as the Actions menu and global shortcuts. "
                + "Clicking a button does not take focus from the application you are working in.",
            TextWrapping = TextWrapping.Wrap,
        });
        var startup = new CheckBox { Content = "Show the Quick Shelf when Power Ops starts", IsChecked = QuickShelfModel.ShowAtStartup(settings), Margin = new Thickness(0, 10, 0, 4) };
        var vertical = new CheckBox { Content = "Vertical", IsChecked = settings.ShelfOrientation == ShelfOrientation.Vertical, Margin = new Thickness(0, 4, 0, 4) };
        var topmost = new CheckBox { Content = "Always on top", IsChecked = settings.ShelfAlwaysOnTop, Margin = new Thickness(0, 4, 0, 4) };
        var autoHide = new CheckBox { Content = "Auto-hide (collapse to the handle when the pointer leaves)", IsChecked = settings.ShelfAutoHide, Margin = new Thickness(0, 4, 0, 10) };
        body.Children.Add(startup);
        body.Children.Add(vertical);
        body.Children.Add(topmost);
        body.Children.Add(autoHide);
        LayoutEditorState layout = AddLayoutEditor(body, ActionSurface.QuickShelf, settings);

        Window dialog = SessionDialogWindow("Customize Quick Shelf", body);
        dialog.Height = 720;
        body.Children.Add(SessionButton("Save", () =>
        {
            string? failure = TryUpdateQuickActionSettings(s =>
            {
                if (startup.IsChecked == true) s.Mode = InteractionMode.QuickShelf;
                else if (s.Mode == InteractionMode.QuickShelf) s.Mode = InteractionMode.Off;
                s.ShelfOrientation = vertical.IsChecked == true ? ShelfOrientation.Vertical : ShelfOrientation.Horizontal;
                s.ShelfAlwaysOnTop = topmost.IsChecked == true;
                s.ShelfAutoHide = autoHide.IsChecked == true;
                ApplyLayoutEditor(s, ActionSurface.QuickShelf, layout);
            });
            if (failure is null) dialog.DialogResult = true;
            else layout.Error.Text = failure;
        }));

        if (SessionDialog(dialog))
        {
            _viewModel.StatusText = "Quick Shelf saved";
            _shelf?.EnsureOnScreen();
        }
    });
}
