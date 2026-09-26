using System.Windows.Input;
using JUtility.Core.Actions;
using JUtility.Core.Models;

namespace JUtility.App;

// V2.4: the mouse's own Back/Forward buttons switch Power Ops tabs, and Esc puts Power Ops away, with no Logi Options+
// application-specific setup (Options+ could not find JUtilityPalette.exe during the MX acceptance). A mouse button
// remapped to Esc in Options+ (for example the middle button) therefore also closes Power Ops.
public partial class MainWindow
{
    private void InitializeMouseKeys()
    {
        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2) || _sessionShell is null) return;
            if (SummonHookOwnsBackForward()) return;
            e.Handled = true;
            RunQuickAction(e.ChangedButton == MouseButton.XButton1 ? QuickActionCatalog.TabPrevious : QuickActionCatalog.TabNext, ActionSurface.InAppShortcut);
        };

        // Bubbling, so anything that uses Esc itself (search box clear, open drop-downs, menus, dialogs) keeps it.
        KeyDown += (_, e) =>
        {
            if (e.Handled || e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None || !IsVisible) return;
            e.Handled = true;
            HideToBackground();
        };
    }

    /// <summary>In Summon mode bound to mouse button 4 or 5, that button already shows/hides Power Ops through the hook.</summary>
    private bool SummonHookOwnsBackForward() =>
        _viewModel.WindowBehavior == WindowBehaviorMode.Summon
        && _viewModel.SummonMouseBinding is SummonMouseBinding.MouseButton4 or SummonMouseBinding.MouseButton5;
}
