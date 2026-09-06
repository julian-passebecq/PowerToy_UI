using System.Diagnostics;
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
