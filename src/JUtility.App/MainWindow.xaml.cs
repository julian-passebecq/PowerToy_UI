using System.Diagnostics;
using System.Windows;
using JUtility.App.ViewModels;
using JUtility.Core.Models;
using JUtility.Core.Services;
using Microsoft.Win32;

namespace JUtility.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        Loaded += (_, _) =>
        {
            ApplyViewMode(_viewModel.ViewMode);
            ApplyPin();
            ApplyExtraColumnVisibility();
        };

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

    private void Pin_Changed(object sender, RoutedEventArgs e)
    {
        ApplyPin();
        SafeSave();
    }

    private void ApplyPin() => Topmost = _viewModel.AlwaysOnTop;

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
            MessageBox.Show(this, "This value is not a valid HTTP/HTTPS URL.", "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            string text = string.IsNullOrWhiteSpace(note.Title)
                ? note.Text
                : note.Title.Trim() + Environment.NewLine + note.Text.Trim();
            CopyText(text, "Note copied");
        }
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

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                _viewModel.Export(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                _viewModel.Import(dialog.FileName);
                ApplyViewMode(_viewModel.ViewMode);
                ApplyPin();
                ApplyExtraColumnVisibility();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
