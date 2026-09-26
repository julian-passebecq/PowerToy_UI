using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JUtility.Core.Capture;
using JUtility.Core.Credentials;
using JUtility.Core.Models;

namespace JUtility.App;

// "+ -> type/paste -> Save": a small standalone window over whatever app is focused. It creates ordinary
// Capture entries (or routes a clipboard image through the existing managed media path). Project and labels
// are optional; classification can happen later in Capture. The main window is not brought forward.
public partial class MainWindow
{
    private Window? _quickCaptureWindow;

    private void ShowQuickCapture()
    {
        if (_quickCaptureWindow is { IsLoaded: true })
        {
            _quickCaptureWindow.Activate();
            return;
        }

        var kinds = new (CaptureKind? Kind, string Label)[]
        {
            (CaptureKind.QuickNote, "Note"), (CaptureKind.Bookmark, "Link"), (CaptureKind.Todo, "To-do"),
            (CaptureKind.ReadLater, "Read later"), (null, "Clipboard image"),
        };
        bool clipboardImage = false;
        try { clipboardImage = Clipboard.ContainsImage(); } catch (System.Runtime.InteropServices.COMException) { }

        var body = new DockPanel { Margin = new Thickness(14) };
        var kindBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(kindBar, Dock.Top);
        body.Children.Add(kindBar);

        var text = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxLength = 20_000,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14,
            ToolTip = "Type or paste. Ctrl+Enter saves, Esc cancels.",
        };
        AutomationProperties.SetName(text, "Capture text");

        var options = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(options, Dock.Bottom);
        body.Children.Add(options);
        body.Children.Add(text);

        var hint = new TextBlock { Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
        options.Children.Add(hint);
        var row = new WrapPanel();
        row.Children.Add(new TextBlock { Text = "Project", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var projects = new List<(Guid? Id, string Name)> { (null, "(none)") };
        projects.AddRange(_viewModel.Projects.Where(x => !x.IsArchived).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => ((Guid?)x.Id, x.Name)));
        var project = new ComboBox { ItemsSource = projects.Select(x => x.Name).ToList(), SelectedIndex = 0, MinWidth = 150, Margin = new Thickness(0, 2, 10, 2) };
        AutomationProperties.SetName(project, "Project (optional)");
        row.Children.Add(project);
        row.Children.Add(new TextBlock { Text = "Labels", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var labels = new TextBox { MinWidth = 120, MaxLength = 200, Margin = new Thickness(0, 2, 0, 2), ToolTip = "Optional, comma separated" };
        AutomationProperties.SetName(labels, "Labels (optional)");
        row.Children.Add(labels);
        options.Children.Add(row);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        var save = new Button { Content = "Save  (Ctrl+Enter)", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(3), IsDefault = false };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(3), IsCancel = true };
        buttons.Children.Add(save); buttons.Children.Add(cancel);
        options.Children.Add(buttons);

        CaptureKind? chosen = null;   // null + imageMode = clipboard image
        bool imageMode = false, userPicked = false, syncing = false;
        var toggles = new List<ToggleButton>();
        void Select(CaptureKind? kind, bool image)
        {
            chosen = kind; imageMode = image;
            syncing = true;
            try { for (int i = 0; i < toggles.Count; i++) toggles[i].IsChecked = kinds[i].Kind == kind && (kinds[i].Kind is not null || image); }
            finally { syncing = false; }
            text.ToolTip = image ? "Optional title for the image" : "Type or paste. Ctrl+Enter saves, Esc cancels.";
        }
        foreach (var (kind, label) in kinds)
        {
            var toggle = new ToggleButton { Content = label, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(2), IsEnabled = kind is not null || clipboardImage };
            AutomationProperties.SetName(toggle, "Capture as " + label);
            // Checked/Unchecked (not Click) so mouse, keyboard and the UI Automation Toggle pattern behave the same.
            toggle.Checked += (_, _) => { if (syncing) return; userPicked = true; Select(kind, kind is null); };
            toggle.Unchecked += (_, _) => { if (!syncing) Select(chosen, imageMode); }; // one kind always stays selected
            toggles.Add(toggle);
            kindBar.Children.Add(toggle);
        }
        Select(CaptureKind.QuickNote, false);

        text.TextChanged += (_, _) =>
        {
            if (!userPicked) Select(QuickCaptureRules.Suggest(text.Text), false);
            string? reason = imageMode ? null : SecretHeuristics.Reason(text.Text);
            hint.Text = reason is null ? "" : $"This text {reason}. Secrets belong in Credentials & IDs (Windows Credential Manager), not in captures.";
        };

        var window = new Window
        {
            Title = "Quick capture", Width = 480, Height = 330, MinWidth = 360, MinHeight = 250, Content = body,
            Topmost = true, ShowInTaskbar = true, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brushes.White,
        };
        // Deliberately unowned: the main window may be hidden (summon mode) and must not take this window with it.
        void Save()
        {
            try
            {
                Guid? projectId = projects[Math.Max(0, project.SelectedIndex)].Id;
                if (imageMode) SaveQuickImage(text.Text, projectId, labels.Text);
                else SaveQuickNote(chosen ?? CaptureKind.QuickNote, text.Text, projectId, labels.Text);
                window.Close();
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                hint.Text = ex.Message;
            }
        }
        save.Click += (_, _) => Save();
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Save(); }
        };
        window.Loaded += (_, _) => { text.Focus(); Keyboard.Focus(text); };
        window.Closed += (_, _) => _quickCaptureWindow = null;
        _quickCaptureWindow = window;
        window.Show();
        window.Activate();
    }

    private void SaveQuickNote(CaptureKind kind, string text, Guid? projectId, string labels)
    {
        var note = QuickCaptureRules.Create(kind, text, projectId, labels, DateTimeOffset.UtcNow);
        _viewModel.AddCapture(note);
        RefreshAfterDataChange();
        if (!SafeSave())
        {
            _viewModel.RemoveNote(note);
            RefreshAfterDataChange();
            throw new InvalidOperationException("The capture was not saved because the workspace could not be written. Nothing was changed.");
        }
        _viewModel.StatusText = "Captured to " + (kind switch { CaptureKind.Todo => "To-do", CaptureKind.Bookmark => "Links", CaptureKind.ReadLater => "Read later", _ => "Quick notes" }) + ": " + note.Title;
    }

    private void SaveQuickImage(string title, Guid? projectId, string labels)
    {
        if (!Clipboard.ContainsImage()) throw new InvalidOperationException("The clipboard no longer contains an image.");
        BitmapSource image = Clipboard.GetImage() ?? throw new InvalidOperationException("The clipboard image could not be read.");
        ClipboardMediaEntry media = _mediaStorage.SaveClipboardImage(image);
        string firstLine = title.Replace("\r\n", "\n").Split('\n')[0].Trim();
        if (firstLine.Length > 0) media.Title = firstLine.Length > 120 ? firstLine[..120] : firstLine;
        media.ProjectId = projectId;
        media.Tags = labels.Replace('\n', ' ').Replace('\r', ' ').Trim();
        _viewModel.AddClipboardMedia(media);
        RefreshAfterDataChange();
        if (!SafeSave())
        {
            _viewModel.RemoveClipboardMedia(media);
            try { _mediaStorage.DeleteManagedFile(media); } catch { /* An orphaned file is safer than losing workspace state. */ }
            RefreshAfterDataChange();
            throw new InvalidOperationException("The image was not saved because the workspace could not be written. Nothing was changed.");
        }
        _viewModel.StatusText = "Image captured to the Clipboard library: " + media.Title;
    }

    private void ExportAtlasNoteHandoff() => SessionAction(() =>
    {
        if (!SafeSave(showError: true)) return;
        var candidates = _viewModel.Notes.Where(x => !x.IsArchived && x.Kind != CaptureKind.Transcript)
            .OrderByDescending(x => x.UpdatedUtc).ToList();
        if (candidates.Count == 0) { ShowOwnedMessage("There are no active captures to hand off.", "AtlasNote handoff", MessageBoxImage.Information); return; }
        var warnings = AtlasNoteHandoff.Review(candidates).ToDictionary(x => x.NoteId, x => x.Reason);
        var projectNames = _viewModel.Projects.ToDictionary(x => x.Id, x => x.Name);

        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = "Choose captures for a one-way, reviewed handoff file (powerops.atlasnote-handoff/1). It carries titles, text, links, labels, "
                + "status and stable source IDs. No credentials, .env data or media files. Power Ops keeps every capture; nothing is moved or deleted. "
                + "Items that look like they contain a secret start unchecked.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });
        var list = new StackPanel();
        var boxes = new List<(CheckBox Box, StickyNoteEntry Note)>();
        foreach (var note in candidates)
        {
            string project = note.ProjectId is Guid id && projectNames.TryGetValue(id, out string? name) ? " · " + name : "";
            var box = new CheckBox
            {
                Content = $"[{AtlasNoteHandoff.KindName(note.Kind)}] {note.Title}{project}" + (warnings.TryGetValue(note.Id, out string? reason) ? $"  ⚠ {reason}" : ""),
                IsChecked = !warnings.ContainsKey(note.Id), Margin = new Thickness(0, 2, 0, 2),
            };
            if (warnings.ContainsKey(note.Id)) box.Foreground = Brushes.DarkOrange;
            AutomationProperties.SetName(box, $"Include {note.Title}");
            boxes.Add((box, note));
            list.Children.Add(box);
        }
        var selectRow = new WrapPanel();
        selectRow.Children.Add(SessionButton("Select all", () => boxes.ForEach(x => x.Box.IsChecked = true)));
        selectRow.Children.Add(SessionButton("Select none", () => boxes.ForEach(x => x.Box.IsChecked = false)));
        body.Children.Add(selectRow);
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Window dialog = SessionDialogWindow("AtlasNote handoff (review)", body);
        dialog.Width = 620;
        body.Children.Add(SessionButton("Export selected...", () => { if (boxes.Any(x => x.Box.IsChecked == true)) dialog.DialogResult = true; }));
        if (!SessionDialog(dialog)) return;

        var selected = boxes.Where(x => x.Box.IsChecked == true).Select(x => x.Note).ToList();
        string json = AtlasNoteHandoff.Create(selected, projectNames, DateTimeOffset.UtcNow);
        if (WriteSessionExport(json, $"PowerOps-atlasnote-handoff-{DateTime.Now:yyyyMMdd-HHmm}.json"))
            _viewModel.StatusText += $" · {selected.Count} captures; originals kept in Power Ops";
    });
}
