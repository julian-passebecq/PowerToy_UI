using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JUtility.Core.Actions;
using JUtility.Core.Models;

namespace JUtility.App;

// V2.1 Interaction settings: choose how Quick Actions are reached and get MX Master / Logi Options+ steps
// built from the shortcuts that are actually configured. Everything is edited on a copy and saved at once.
public partial class MainWindow
{
    private void EditInteraction() => SessionAction(() =>
    {
        if (_quickActionSettings is null || _quickActionStore is null)
        {
            ShowOwnedMessage(ShelfUnavailable()!, "Interaction settings", MessageBoxImage.Warning);
            return;
        }

        BringToFront();
        QuickActionSettings draft = QuickActionSettingsStore.Copy(_quickActionSettings);
        InteractionMode mode = draft.Mode;
        Action refresh = () => { }; // assigned once every control below exists

        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = "Choose how you reach Power Ops actions. Every option uses the same actions; nothing here needs Logitech hardware.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var modes = new StackPanel();
        body.Children.Add(modes);
        var warning = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xCE)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xC4, 0x5A)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 4),
        };
        var warningText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var warningBody = new StackPanel();
        warningBody.Children.Add(warningText);
        warningBody.Children.Add(SessionButton("Use Ctrl + middle click for Summon (applies now)", () =>
        {
            _viewModel.SummonMouseBinding = SummonMouseBinding.CtrlMiddleClick;
            SafeSave();
            if (_viewModel.WindowBehavior == WindowBehaviorMode.Summon) ApplyWindowBehavior();
            refresh();
        }));
        warning.Child = warningBody;
        body.Children.Add(warning);

        body.Children.Add(new TextBlock { Text = "Shortcuts for this mode", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        var shortcuts = new StackPanel();
        body.Children.Add(shortcuts);
        var report = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 4) };
        var addRecommended = SessionButton("Add recommended shortcuts", () =>
        {
            // Probes Windows for each candidate; nothing is registered until Save.
            IReadOnlyList<ShortcutPlanLine> lines = InteractionGuide.AddRecommended(draft, mode, gesture => _hotkeys.IsAvailable(gesture));
            report.Text = lines.Count == 0 ? "This mode needs no global shortcut." : string.Join("\n", lines.Select(x => x.Message)) + "\nSaved when you press Save.";
            refresh();
        });
        body.Children.Add(addRecommended);
        body.Children.Add(report);

        var guide = new Expander { Header = "MX Master / Logi Options+ guide", Margin = new Thickness(0, 12, 0, 4) };
        var guideSteps = new StackPanel { Margin = new Thickness(4, 6, 0, 0) };
        guide.Content = guideSteps;
        body.Children.Add(guide);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(error);

        foreach (InteractionMode option in Enum.GetValues<InteractionMode>())
        {
            var radio = new RadioButton
            {
                GroupName = "interactionMode",
                IsChecked = option == mode,
                Margin = new Thickness(0, 4, 0, 0),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = ModeTitle(option), FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = InteractionGuide.Describe(option), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, MaxWidth = 420 },
                    },
                },
            };
            AutomationProperties.SetName(radio, ModeTitle(option));
            radio.Checked += (_, _) => { mode = option; report.Text = string.Empty; refresh(); };
            modes.Children.Add(radio);
        }

        void Refresh()
        {
            string? conflict = QuickActionLayouts.MouseDoubleInterceptionWarning(_viewModel.WindowBehavior, _viewModel.SummonMouseBinding, mode);
            warningText.Text = conflict ?? string.Empty;
            warning.Visibility = conflict is null ? Visibility.Collapsed : Visibility.Visible;

            shortcuts.Children.Clear();
            IReadOnlyList<RecommendedShortcut> recommended = InteractionGuide.Recommended(mode);
            if (recommended.Count == 0)
            {
                shortcuts.Children.Add(new TextBlock { Text = "None needed.", Foreground = Brushes.DimGray });
            }

            foreach (RecommendedShortcut item in recommended)
            {
                ShortcutBinding? binding = draft.GlobalShortcuts.FirstOrDefault(x => x.ActionId == item.ActionId);
                bool active = binding is not null && _hotkeys.Registered.Any(x => x.ActionId == item.ActionId && x.Gesture.ToString() == binding.Gesture);
                string state = binding is null ? "not bound"
                    : !draft.GlobalShortcutsEnabled ? $"{binding.Gesture} (global shortcuts are off)"
                    : active ? $"{binding.Gesture} (active)"
                    : $"{binding.Gesture} (applied on Save)";
                shortcuts.Children.Add(new TextBlock { Text = $"{QuickActionCatalog.Get(item.ActionId).Label}: {state}", TextWrapping = TextWrapping.Wrap });
            }

            addRecommended.IsEnabled = recommended.Count > 0;
            guide.IsExpanded = mode is InteractionMode.MxMasterGuide or InteractionMode.Hybrid;
            guideSteps.Children.Clear();
            int number = 1;
            foreach (GuideStep step in InteractionGuide.MxMasterSteps(draft, conflict))
            {
                var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
                panel.Children.Add(new TextBlock
                {
                    Text = $"{number++}. {step.Title}",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = step.IsWarning ? Brushes.DarkGoldenrod : Brushes.Black,
                    TextWrapping = TextWrapping.Wrap,
                });
                panel.Children.Add(new TextBlock { Text = step.Detail, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
                if (step.CopyText is string text)
                {
                    Button copy = SessionButton($"Copy {text}", () =>
                    {
                        try { Clipboard.SetText(text); error.Text = $"Copied {text}."; }
                        catch (COMException) { error.Text = "The clipboard is busy; try again."; }
                    });
                    copy.HorizontalAlignment = HorizontalAlignment.Left;
                    panel.Children.Add(copy);
                }

                guideSteps.Children.Add(panel);
            }
        }

        Window dialog = SessionDialogWindow("Interaction settings", body);
        dialog.Width = 540;
        dialog.Height = 760;
        body.Children.Add(SessionButton("Save", () =>
        {
            string? failure = TryUpdateQuickActionSettings(s =>
            {
                s.Mode = mode;
                s.GlobalShortcutsEnabled = draft.GlobalShortcutsEnabled;
                s.GlobalShortcuts = draft.GlobalShortcuts.Select(x => new ShortcutBinding { Gesture = x.Gesture, ActionId = x.ActionId }).ToList();
            });
            if (failure is null) dialog.DialogResult = true;
            else error.Text = failure;
        }));
        refresh = Refresh;
        Refresh();

        if (!SessionDialog(dialog))
        {
            return;
        }

        IReadOnlyList<string> failures = ApplyGlobalHotkeys();
        if (QuickShelfModel.ShowAtStartup(_quickActionSettings!)) ShowQuickShelf(forKeyboard: false);
        _viewModel.StatusText = $"Interaction: {ModeTitle(mode)}";
        if (failures.Count > 0) ShowOwnedMessage(_hotkeyStatus, "Power Ops global shortcuts", MessageBoxImage.Warning);
    });

    private static string ModeTitle(InteractionMode mode) => mode switch
    {
        InteractionMode.Off => "Off",
        InteractionMode.QuickShelf => "Quick Shelf",
        InteractionMode.QuickRing => "Quick Ring",
        InteractionMode.MxMasterGuide => "MX Master guide",
        InteractionMode.Hybrid => "Hybrid (recommended)",
        _ => mode.ToString(),
    };
}
