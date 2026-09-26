using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JUtility.Core.Actions;

namespace JUtility.App;

// V2.4 sub-rings: create, rename, fill and delete the "group:" rings (Folders ›, Apps ›...). Every change is saved
// through the same validated quick-actions.json path as the other layout editors.
public partial class MainWindow
{
    private void ManageRingGroups() => SessionAction(() =>
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
            Text = "A sub-ring is a second circle behind one Quick Ring slot, for example Folders › (Downloads, Desktop...) or Apps › "
                + "(VS Code, Gmail, Mongoku...). Put it on the ring with Customize Quick Ring > Add. Apps can be web apps (Actions > Web apps) "
                + "or Tool Launcher entries such as VS Code. Bound to a global shortcut, a sub-ring opens the Quick Ring directly on it.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        var list = new ListBox { MinHeight = 150, Margin = new Thickness(0, 4, 0, 4) };
        AutomationProperties.SetName(list, "Quick Ring sub-rings");
        body.Children.Add(list);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

        void Refresh(int select = -1)
        {
            QuickActionSettings settings = _quickActionSettings!;
            list.ItemsSource = settings.RingGroups.Select(group =>
            {
                string onRing = settings.Ring.Contains(QuickRingGroups.ActionId(group.Id)) ? "on the ring" : "not on the ring";
                string items = string.Join(", ", group.Items.Select(id => DescribeAction(settings, id).Label));
                return $"{group.Name} › ({onRing}): {items}";
            }).ToList();
            if (select >= 0 && select < settings.RingGroups.Count) list.SelectedIndex = select;
            error.Text = string.Empty;
        }

        RingGroup? Selected() => list.SelectedIndex >= 0 && list.SelectedIndex < _quickActionSettings!.RingGroups.Count
            ? _quickActionSettings.RingGroups[list.SelectedIndex]
            : null;

        void Save(Action<QuickActionSettings> change, int select)
        {
            string? failure = TryUpdateQuickActionSettings(change);
            if (failure is null) Refresh(select);
            else error.Text = failure;
        }

        var row = new WrapPanel();
        row.Children.Add(SessionButton("New sub-ring...", () =>
        {
            string? name = AskSessionName("Name of the new sub-ring (for example Apps or Projects)", "");
            if (name is null) return;
            List<string>? items = EditActionList($"{name} › actions", [], _quickActionSettings!);
            if (items is null) return;
            var group = new RingGroup { Id = Guid.NewGuid(), Name = name, Glyph = "E8B7", Items = items };
            Save(s =>
            {
                s.RingGroups.Add(group);
                string slot = QuickRingGroups.ActionId(group.Id);
                if (s.Ring.Count < QuickActionLayouts.MaxRing) s.Ring.Add(slot); // straight onto the ring while there is room
            }, _quickActionSettings!.RingGroups.Count);
        }));
        row.Children.Add(SessionButton("Edit actions...", () =>
        {
            if (Selected() is not { } group) { error.Text = "Select a sub-ring first."; return; }
            int index = list.SelectedIndex;
            List<string>? items = EditActionList($"{group.Name} › actions", group.Items, _quickActionSettings!);
            if (items is not null) Save(s => s.RingGroups[index].Items = items, index);
        }));
        row.Children.Add(SessionButton("Rename...", () =>
        {
            if (Selected() is not { } group) { error.Text = "Select a sub-ring first."; return; }
            int index = list.SelectedIndex;
            string? name = AskSessionName("New name", group.Name);
            if (name is not null) Save(s => s.RingGroups[index].Name = name, index);
        }));
        row.Children.Add(SessionButton("Delete", () =>
        {
            if (Selected() is not { } group) { error.Text = "Select a sub-ring first."; return; }
            if (MessageBox.Show(Window.GetWindow(list), $"Delete the {group.Name} sub-ring? Its actions stay available elsewhere.",
                    "Delete sub-ring", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            Save(s => QuickRingGroups.Remove(s, group.Id), -1);
        }));
        body.Children.Add(row);
        body.Children.Add(error);
        Refresh(0);

        Window dialog = SessionDialogWindow("Quick Ring sub-rings", body);
        dialog.Width = 560;
        body.Children.Add(SessionButton("Close", () => dialog.DialogResult = true));
        SessionDialog(dialog);
        _viewModel.StatusText = "Quick Ring sub-rings saved";
    });

    /// <summary>Ordered pick list for one sub-ring (1 to 8 actions, no nested sub-rings). Null = cancelled.</summary>
    private List<string>? EditActionList(string title, IReadOnlyList<string> initial, QuickActionSettings settings)
    {
        var ids = initial.ToList();
        IReadOnlyList<QuickActionDefinition> eligible = EligibleActions(ActionSurface.QuickRing, settings, includeGroups: false);
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = $"Up to {QuickActionLayouts.MaxRing} actions, numbered clockwise from the top (keys 1-{QuickActionLayouts.MaxRing} in the ring).",
            TextWrapping = TextWrapping.Wrap,
        });
        var list = new ListBox { MinHeight = 170, Margin = new Thickness(0, 6, 0, 4) };
        AutomationProperties.SetName(list, title);
        body.Children.Add(list);
        var addBox = new ComboBox { DisplayMemberPath = nameof(QuickActionDefinition.Label), MinWidth = 220 };
        AutomationProperties.SetName(addBox, "Action to add");
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

        void Refresh(int select = -1)
        {
            list.ItemsSource = ids.Select((id, index) => $"{index + 1}. {DescribeAction(settings, id).Label}").ToList();
            if (select >= 0 && select < ids.Count) list.SelectedIndex = select;
            addBox.ItemsSource = eligible.Where(x => !ids.Contains(x.Id)).ToList();
            addBox.SelectedIndex = addBox.Items.Count > 0 ? 0 : -1;
            error.Text = string.Empty;
        }

        void Move(int delta)
        {
            int index = list.SelectedIndex, target = index + delta;
            if (index < 0 || target < 0 || target >= ids.Count) return;
            (ids[index], ids[target]) = (ids[target], ids[index]);
            Refresh(target);
        }

        var row = new WrapPanel();
        row.Children.Add(SessionButton("Move up", () => Move(-1)));
        row.Children.Add(SessionButton("Move down", () => Move(1)));
        row.Children.Add(SessionButton("Remove", () =>
        {
            int index = list.SelectedIndex;
            if (index < 0) return;
            ids.RemoveAt(index);
            Refresh(Math.Min(index, ids.Count - 1));
        }));
        body.Children.Add(row);
        var addRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        addRow.Children.Add(addBox);
        addRow.Children.Add(SessionButton("Add", () =>
        {
            if (addBox.SelectedItem is not QuickActionDefinition action) return;
            if (ids.Count >= QuickActionLayouts.MaxRing) { error.Text = $"A sub-ring holds at most {QuickActionLayouts.MaxRing} actions."; return; }
            ids.Add(action.Id);
            Refresh(ids.Count - 1);
        }));
        body.Children.Add(addRow);
        body.Children.Add(error);
        Refresh();

        Window dialog = SessionDialogWindow(title, body);
        body.Children.Add(SessionButton("OK", () =>
        {
            if (ids.Count == 0) { error.Text = "Add at least one action."; return; }
            dialog.DialogResult = true;
        }));
        return SessionDialog(dialog) ? ids : null;
    }
}
