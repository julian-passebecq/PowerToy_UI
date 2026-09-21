using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JUtility.App.Services;
using JUtility.App.ViewModels;
using JUtility.Core.Models;
using JUtility.Core.Services;
using Microsoft.Win32;

namespace JUtility.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly GlobalMouseSummonService _summonService = new();
    private bool _temporaryPin;
    private bool _suppressAutoHide;
    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _summonService.Triggered += SummonService_Triggered;
        Loaded += MainWindow_Loaded;
        Deactivated += MainWindow_Deactivated;
        Closing += (_, _) =>
        {
            try
            {
                _viewModel.Save();
            }
            catch
            {
                // Do not block window close if storage is temporarily unavailable.
            }
        };
        Closed += (_, _) => _summonService.Dispose();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        ApplyViewMode(_viewModel.ViewMode);
        ApplyExtraColumnVisibility();
        ApplyWindowBehavior(initialLoad: true);
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (_suppressAutoHide
            || _temporaryPin
            || _viewModel.WindowBehavior != WindowBehaviorMode.Summon
            || !_viewModel.HideOnFocusLoss)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (!_suppressAutoHide
                && !_temporaryPin
                && _viewModel.WindowBehavior == WindowBehaviorMode.Summon
                && _viewModel.HideOnFocusLoss
                && IsVisible
                && !IsActive)
            {
                Hide();
            }
        }));
    }

    private void RepositoryHub_Click(object sender, RoutedEventArgs e) => SelectWorkspaceTab("Repository Hub");
    private void PortalLauncher_Click(object sender, RoutedEventArgs e) => SelectWorkspaceTab("Portals");
    private void Capture_Click(object sender, RoutedEventArgs e) => SelectWorkspaceTab("Capture");
    private void Clipboard_Click(object sender, RoutedEventArgs e) => SelectWorkspaceTab("Clipboard");
    private void PromptBuilder_Click(object sender, RoutedEventArgs e) => SelectWorkspaceTab("Prompt Builder");

    private void SelectWorkspaceTab(string header)
    {
        foreach (object item in WorkspacePanel.Items)
        {
            if (item is TabItem tab && string.Equals(tab.Header?.ToString(), header, StringComparison.Ordinal))
            {
                WorkspacePanel.SelectedItem = tab;
                return;
            }
        }
    }

    private void Sidebar_Click(object sender, RoutedEventArgs e) => SetViewMode(WorkspaceViewMode.Sidebar);
    private void Compact_Click(object sender, RoutedEventArgs e) => SetViewMode(WorkspaceViewMode.Compact);
    private void Expanded_Click(object sender, RoutedEventArgs e) => SetViewMode(WorkspaceViewMode.Expanded);

    private void SetViewMode(WorkspaceViewMode mode)
    {
        _viewModel.ViewMode = mode;
        ApplyViewMode(mode);
        SafeSave();
    }

    private void ApplyViewMode(WorkspaceViewMode mode)
    {
        if (mode == WorkspaceViewMode.Sidebar)
        {
            SidebarPanel.Visibility = Visibility.Visible;
            WorkspacePanel.Visibility = Visibility.Collapsed;
            Width = 390;
            Height = Math.Max(Height, 700);
        }
        else
        {
            SidebarPanel.Visibility = Visibility.Collapsed;
            WorkspacePanel.Visibility = Visibility.Visible;
            Width = mode == WorkspaceViewMode.Compact ? 900 : 1280;
            Height = mode == WorkspaceViewMode.Compact ? 760 : 850;
        }
    }

    private void WindowBehavior_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedValue is WindowBehaviorMode mode)
        {
            _viewModel.WindowBehavior = mode;
        }

        if (!_loaded)
        {
            return;
        }

        ApplyWindowBehavior();
        SafeSave();
    }

    private void SummonBinding_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedValue is SummonMouseBinding binding)
        {
            _viewModel.SummonMouseBinding = binding;
        }

        if (!_loaded)
        {
            return;
        }

        if (_viewModel.WindowBehavior == WindowBehaviorMode.Summon)
        {
            ApplyWindowBehavior();
        }

        SafeSave();
    }

    private void SummonPreference_Changed(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            SafeSave();
        }
    }

    private void TemporaryPin_Changed(object sender, RoutedEventArgs e)
    {
        _temporaryPin = TemporaryPinCheck.IsChecked == true;
        ApplyTopmost();
        _viewModel.StatusText = _temporaryPin ? "Kept open temporarily" : "Temporary pin released";
    }

    private void ApplyWindowBehavior(bool initialLoad = false)
    {
        bool summonMode = _viewModel.WindowBehavior == WindowBehaviorMode.Summon;
        ShowInTaskbar = !summonMode;
        ApplyTopmost();

        if (!summonMode)
        {
            _summonService.Stop();
            return;
        }

        try
        {
            _summonService.Start(_viewModel.SummonMouseBinding);
            _viewModel.StatusText = "Summon mode ready";

            if (initialLoad)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    if (_viewModel.WindowBehavior == WindowBehaviorMode.Summon && _summonService.IsRunning)
                    {
                        Hide();
                    }
                }));
            }
        }
        catch (Exception ex)
        {
            _viewModel.WindowBehavior = WindowBehaviorMode.Normal;
            ShowInTaskbar = true;
            ApplyTopmost();
            _viewModel.StatusText = "Summon mode unavailable";
            ShowOwnedMessage(ex.Message, "Summon mode unavailable", MessageBoxImage.Warning);
        }
    }

    private void ApplyTopmost() =>
        Topmost = _viewModel.WindowBehavior == WindowBehaviorMode.AlwaysOnTop || _temporaryPin;

    private void SummonService_Triggered(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(ToggleSummonVisibility));

    private void ToggleSummonVisibility()
    {
        if (_viewModel.WindowBehavior != WindowBehaviorMode.Summon)
        {
            return;
        }

        if (IsVisible)
        {
            Hide();
            return;
        }

        ShowSummoned();
    }

    private void ShowSummoned()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_viewModel.OpenNearCursor)
            {
                WindowPlacementService.MoveNearCursor(this);
            }

            Activate();
            Focus();
        }));
    }

    private void HideNow_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.WindowBehavior != WindowBehaviorMode.Summon)
        {
            _viewModel.StatusText = "Select Summon / hide mode first";
            return;
        }

        Hide();
    }

    private void ShowExtra_Changed(object sender, RoutedEventArgs e)
    {
        ApplyExtraColumnVisibility();
        SafeSave();
    }

    private void ApplyExtraColumnVisibility() =>
        ExtraUrlColumn.Visibility = _viewModel.ShowExtraColumn ? Visibility.Visible : Visibility.Collapsed;

    private void Save_Click(object sender, RoutedEventArgs e) => SafeSave(showError: true);

    private void AddProject_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddProject();
        SafeSave();
    }

    private void AddPortal_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddPortal();
        SafeSave();
    }

    private void DeletePortal_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPortal is not PortalEntry portal)
        {
            _viewModel.StatusText = "No portal selected";
            return;
        }

        _viewModel.RemovePortal(portal);
        SafeSave();
    }

    private void AddPortalLink_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPortal is not PortalEntry portal)
        {
            _viewModel.StatusText = "Create or select a portal first";
            return;
        }

        _viewModel.AddPortalLink(portal);
        PortalLinksGrid.Items.Refresh();
        SafeSave();
    }

    private void AddSnippet_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddClipboardSnippet();
        SafeSave();
    }

    private void DeleteSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSnippet is not ClipboardSnippetEntry snippet)
        {
            _viewModel.StatusText = "No clipboard snippet selected";
            return;
        }

        _viewModel.RemoveClipboardSnippet(snippet);
        SafeSave();
    }

    private void CopySnippet_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSnippet is ClipboardSnippetEntry snippet)
        {
            CopyText(snippet.Text, "Clipboard snippet copied");
        }
    }

    private void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ProjectEntry project)
        {
            _viewModel.RemoveProject(project);
            SafeSave();
        }
    }

    private void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ProjectEntry project)
        {
            CopyText(ProjectClipboardFormatter.FormatRow(project), "Project row copied");
        }
    }

    private void CopyValue_Click(object sender, RoutedEventArgs e)
    {
        string? value = (sender as FrameworkElement)?.Tag as string;
        CopyText(value ?? string.Empty, "Value copied");
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e) =>
        CopyText(ProjectClipboardFormatter.FormatAll(_viewModel.Projects), "Included projects copied");

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        string? url = (sender as FrameworkElement)?.Tag as string;
        if (string.IsNullOrWhiteSpace(url))
        {
            _viewModel.StatusText = "No URL configured";
            return;
        }

        if (!UrlNormalizer.TryNormalizeOptionalWebUrl(url, out string normalized) || string.IsNullOrWhiteSpace(normalized))
        {
            ShowOwnedMessage("This value is not a valid HTTP/HTTPS URL.", "Invalid URL", MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(normalized) { UseShellExecute = true });
        _viewModel.StatusText = "Opened link";
    }

    private void AddModule_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddPromptModule();
        SafeSave();
    }

    private void MoveModuleUp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PromptModuleEntry module)
        {
            _viewModel.MoveModule(module, -1);
        }
    }

    private void MoveModuleDown_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PromptModuleEntry module)
        {
            _viewModel.MoveModule(module, 1);
        }
    }

    private void PreviewPrompt_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ComposePrompt(appendProjectLinks: false);
        _viewModel.StatusText = "Prompt preview refreshed";
    }

    private void ComposeCopy_Click(object sender, RoutedEventArgs e)
    {
        string text = _viewModel.ComposePrompt(appendProjectLinks: false);
        if (CopyText(text, "Prompt copied"))
        {
            _viewModel.RecordRecentPrompt(text);
            SafeSave();
        }
    }

    private void ComposeCopyProject_Click(object sender, RoutedEventArgs e)
    {
        string text = _viewModel.ComposePrompt(appendProjectLinks: true);
        if (CopyText(text, "Prompt + project copied"))
        {
            _viewModel.RecordRecentPrompt(text);
            SafeSave();
        }
    }

    private void AddNote_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddNote();
        SafeSave();
    }

    private void CopyNote_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNote is StickyNoteEntry note)
        {
            List<string> parts = [];
            if (!string.IsNullOrWhiteSpace(note.Title)) parts.Add(note.Title.Trim());
            if (!string.IsNullOrWhiteSpace(note.Subject)) parts.Add("Subject: " + note.Subject.Trim());
            if (!string.IsNullOrWhiteSpace(note.Url)) parts.Add(note.Url.Trim());
            if (!string.IsNullOrWhiteSpace(note.Text)) parts.Add(note.Text.Trim());
            CopyText(string.Join(Environment.NewLine, parts), "Capture copied");
        }
    }

    private void ExportSelectedCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNote is not StickyNoteEntry note)
        {
            _viewModel.StatusText = "No capture selected";
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "Markdown files (*.md)|*.md|JSON files (*.json)|*.json",
            FileName = string.IsNullOrWhiteSpace(note.Title) ? "capture.md" : SanitizeFileName(note.Title) + ".md",
        };

        _suppressAutoHide = true;
        try
        {
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            if (string.Equals(Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(note, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                List<string> lines =
                [
                    "# " + (string.IsNullOrWhiteSpace(note.Title) ? "Capture" : note.Title.Trim()),
                    "",
                    $"- Type: {note.Kind}",
                ];
                if (!string.IsNullOrWhiteSpace(note.Subject)) lines.Add("- Subject: " + note.Subject.Trim());
                if (!string.IsNullOrWhiteSpace(note.Status)) lines.Add("- Status: " + note.Status.Trim());
                if (!string.IsNullOrWhiteSpace(note.Priority)) lines.Add("- Priority: " + note.Priority.Trim());
                if (!string.IsNullOrWhiteSpace(note.Url)) lines.Add("- URL: " + note.Url.Trim());
                if (!string.IsNullOrWhiteSpace(note.Labels)) lines.Add("- Labels: " + note.Labels.Trim());
                lines.Add("");
                lines.Add(note.Text ?? string.Empty);
                File.WriteAllText(dialog.FileName, string.Join(Environment.NewLine, lines));
            }

            _viewModel.StatusText = "Capture exported";
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(ex.Message, "Capture export failed", MessageBoxImage.Error);
        }
        finally
        {
            _suppressAutoHide = false;
        }
    }

    private static string SanitizeFileName(string value)
    {
        string safe = value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '-');
        }
        return safe.Length == 0 ? "capture" : safe;
    }

    private void ArchiveNote_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNote is StickyNoteEntry note)
        {
            _viewModel.ArchiveOrRestoreNote(note);
            SafeSave();
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_viewModel.DataDirectory) { UseShellExecute = true });

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "j-utility-workspace.json",
        };

        _suppressAutoHide = true;
        try
        {
            if (dialog.ShowDialog(this) == true)
            {
                _viewModel.Export(dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(ex.Message, "Export failed", MessageBoxImage.Error);
        }
        finally
        {
            _suppressAutoHide = false;
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };

        _suppressAutoHide = true;
        try
        {
            if (dialog.ShowDialog(this) == true)
            {
                _viewModel.Import(dialog.FileName);
                ApplyViewMode(_viewModel.ViewMode);
                ApplyWindowBehavior();
                ApplyExtraColumnVisibility();
            }
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(ex.Message, "Import failed", MessageBoxImage.Error);
        }
        finally
        {
            _suppressAutoHide = false;
        }
    }

    private bool CopyText(string text, string successStatus)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _viewModel.StatusText = "Nothing to copy";
            return false;
        }

        Clipboard.SetText(text);
        _viewModel.StatusText = successStatus;
        return true;
    }

    private void ShowOwnedMessage(string message, string title, MessageBoxImage image)
    {
        bool previous = _suppressAutoHide;
        _suppressAutoHide = true;
        try
        {
            MessageBox.Show(this, message, title, MessageBoxButton.OK, image);
        }
        finally
        {
            _suppressAutoHide = previous;
        }
    }

    private void SafeSave(bool showError = false)
    {
        try
        {
            _viewModel.Save();
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "Save failed";
            if (showError)
            {
                ShowOwnedMessage(ex.Message, "Save failed", MessageBoxImage.Error);
            }
        }
    }
}
