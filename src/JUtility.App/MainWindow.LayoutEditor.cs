using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JUtility.Core.Actions;

namespace JUtility.App;

// Shared "which buttons, in which order, for which workspace" editor used by the Quick Shelf and Quick Ring dialogs.
public partial class MainWindow
{
    private sealed class LayoutEditorState(Guid workspaceId, List<string> defaults, List<string>? workspace, TextBlock error)
    {
        public Guid WorkspaceId { get; } = workspaceId;
        public List<string> Defaults { get; } = defaults;
        /// <summary>Null = the workspace inherits <see cref="Defaults"/>.</summary>
        public List<string>? Workspace { get; set; } = workspace;
        public TextBlock Error { get; } = error;
    }

    private LayoutEditorState AddLayoutEditor(StackPanel body, ActionSurface surface, QuickActionSettings settings)
    {
        bool ring = surface == ActionSurface.QuickRing;
        string surfaceName = ring ? "Quick Ring" : "Quick Shelf";
        string noun = ring ? "slots" : "buttons";
        int max = ring ? QuickActionLayouts.MaxRing : QuickActionLayouts.MaxShelf;
        Guid workspaceId = _sessionShell?.ActiveWorkspaceId ?? Guid.Empty;
        string workspaceName = _sessionShell is null ? string.Empty : CurrentWorkspace().Name;
        WorkspaceActionOverride? saved = settings.WorkspaceOverrides.FirstOrDefault(x => x.WorkspaceId == workspaceId);
        List<string>? savedList = ring ? saved?.Ring : saved?.Shelf;
        var state = new LayoutEditorState(
            workspaceId,
            [.. ring ? settings.Ring : settings.Shelf],
            savedList is null ? null : [.. savedList],
            new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap });
        IReadOnlyList<QuickActionDefinition> eligible = QuickActionLayouts.Eligible(surface, settings)
            .Where(x => _quickActions.IsRegistered(x.Id))
            .ToList();

        string group = surfaceName + "Scope";
        var defaultScope = new RadioButton { Content = $"Default {noun} (all workspaces)", IsChecked = true, GroupName = group };
        var workspaceScope = new RadioButton
        {
            Content = _sessionShell is null ? "This workspace only (workspace views unavailable)" : $"Only for workspace \"{workspaceName}\"",
            GroupName = group,
            IsEnabled = _sessionShell is not null,
        };
        body.Children.Add(defaultScope);
        body.Children.Add(workspaceScope);
        var scopeNote = new TextBlock { Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        body.Children.Add(scopeNote);

        var list = new ListBox { MinHeight = 170, DisplayMemberPath = nameof(QuickActionDefinition.Label), Margin = new Thickness(0, 4, 0, 4) };
        AutomationProperties.SetName(list, $"{surfaceName} {noun}");
        body.Children.Add(list);
        var addBox = new ComboBox { DisplayMemberPath = nameof(QuickActionDefinition.Label), MinWidth = 200 };
        AutomationProperties.SetName(addBox, "Action to add");

        bool editingWorkspace() => workspaceScope.IsChecked == true;
        List<string> shown() => editingWorkspace() ? state.Workspace ?? state.Defaults : state.Defaults;
        List<string> editable() => !editingWorkspace() ? state.Defaults : state.Workspace ??= [.. state.Defaults];

        void Refresh(int select = -1)
        {
            List<string> ids = shown();
            list.ItemsSource = ids.Select((id, index) =>
            {
                QuickActionDefinition action = QuickActionLayouts.Describe(settings, id);
                return ring ? action with { Label = $"{index + 1}. {action.Label}" } : action; // Ring slots are numbered (digit keys).
            }).ToList();
            if (select >= 0 && select < ids.Count) list.SelectedIndex = select;
            addBox.ItemsSource = eligible.Where(x => !ids.Contains(x.Id)).ToList();
            addBox.SelectedIndex = addBox.Items.Count > 0 ? 0 : -1;
            scopeNote.Text = editingWorkspace()
                ? state.Workspace is null ? $"\"{workspaceName}\" uses the default {noun}. Editing below creates its own order." : $"\"{workspaceName}\" has its own {noun} ({ids.Count}/{max})."
                : $"Default {noun} ({ids.Count}/{max}). Workspaces without their own list use these.";
            state.Error.Text = string.Empty;
        }

        void Move(int delta)
        {
            int index = list.SelectedIndex;
            if (index < 0) return;
            List<string> ids = editable();
            int target = index + delta;
            if (target < 0 || target >= ids.Count) return;
            (ids[index], ids[target]) = (ids[target], ids[index]);
            Refresh(target);
        }

        defaultScope.Checked += (_, _) => Refresh();
        workspaceScope.Checked += (_, _) => Refresh();

        var editRow = new WrapPanel();
        editRow.Children.Add(SessionButton("Move up", () => Move(-1)));
        editRow.Children.Add(SessionButton("Move down", () => Move(1)));
        editRow.Children.Add(SessionButton("Remove", () =>
        {
            int index = list.SelectedIndex;
            if (index < 0) return;
            List<string> ids = editable();
            if (ids.Count <= 1) { state.Error.Text = $"The {surfaceName} needs at least one action."; return; }
            ids.RemoveAt(index);
            Refresh(Math.Min(index, ids.Count - 1));
        }));
        editRow.Children.Add(SessionButton("Use default for this workspace", () =>
        {
            state.Workspace = null;
            Refresh();
        }));
        body.Children.Add(editRow);

        var addRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        addRow.Children.Add(addBox);
        addRow.Children.Add(SessionButton("Add", () =>
        {
            if (addBox.SelectedItem is not QuickActionDefinition action) return;
            List<string> ids = editable();
            if (ids.Count >= max) { state.Error.Text = $"The {surfaceName} holds at most {max} {noun}."; return; }
            ids.Add(action.Id);
            Refresh(ids.Count - 1);
        }));
        body.Children.Add(addRow);
        body.Children.Add(state.Error);
        Refresh();
        return state;
    }

    /// <summary>Writes an editor's result into a settings copy (validated by the caller's save).</summary>
    private static void ApplyLayoutEditor(QuickActionSettings settings, ActionSurface surface, LayoutEditorState state)
    {
        if (surface == ActionSurface.QuickRing)
        {
            settings.Ring = [.. state.Defaults];
            if (state.WorkspaceId != Guid.Empty) QuickActionLayouts.SetWorkspaceRing(settings, state.WorkspaceId, state.Workspace);
        }
        else
        {
            settings.Shelf = [.. state.Defaults];
            if (state.WorkspaceId != Guid.Empty) QuickActionLayouts.SetWorkspaceShelf(settings, state.WorkspaceId, state.Workspace);
        }
    }
}
