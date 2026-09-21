using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using JUtility.App.Services;
using JUtility.App.ViewModels;
using JUtility.Core.Models;
using JUtility.Core.Services;
using Microsoft.Win32;

namespace JUtility.App;

public sealed record ShellNavItem(string Key, string Label, int Count, bool IsActive)
{
    public FontWeight Weight => IsActive ? FontWeights.SemiBold : FontWeights.Normal;
}

public sealed record QuickRibbonItem(string Action, string Key, string Title, string Subtitle, string Glyph, string Accent, string Value = "");
public sealed record CaptureBoardSection(string Key, string Title, int Count, IReadOnlyList<StickyNoteEntry> Items);
public sealed record ProjectTreeNode(string Key, string Label, int Count, IReadOnlyList<ProjectTreeNode> Children);

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly GlobalMouseSummonService _summonService = new();
    private bool _temporaryPin;
    private bool _suppressAutoHide;
    private bool _loaded;
    private bool _refreshingProjectSelection;
    private readonly ObservableCollection<ShellNavItem> _secondaryNavItems = [];
    private readonly ObservableCollection<QuickRibbonItem> _quickRibbonItems = [];
    private readonly ObservableCollection<CaptureBoardSection> _captureBoardSections = [];
    private readonly ObservableCollection<ProjectTreeNode> _projectTreeNodes = [];
    private readonly Dictionary<string, string> _moduleFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeRepositoryFamilies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeCaptureSubjects = new(StringComparer.OrdinalIgnoreCase);
    private ICollectionView? _projectView;
    private ICollectionView? _portalView;
    private ICollectionView? _captureView;
    private ICollectionView? _snippetView;
    private ICollectionView? _promptView;
    private string _activeModule = "Dashboard";
    private string _repositorySearchText = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        SecondaryNav.ItemsSource = _secondaryNavItems;
        QuickRibbon.ItemsSource = _quickRibbonItems;
        CaptureBoard.ItemsSource = _captureBoardSections;
        RepositoryTree.ItemsSource = _projectTreeNodes;

        _summonService.Triggered += SummonService_Triggered;
        Loaded += MainWindow_Loaded;
        Deactivated += MainWindow_Deactivated;
        Closing += MainWindow_Closing;
        Closed += (_, _) => _summonService.Dispose();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            _viewModel.Save();
        }
        catch (Exception ex)
        {
            bool previous = _suppressAutoHide;
            _suppressAutoHide = true;
            try
            {
                MessageBoxResult result = MessageBox.Show(
                    this,
                    $"The workspace could not be saved.\n\n{ex.Message}\n\nClose anyway and keep the last successfully saved workspace?",
                    "Save failed",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                }
            }
            finally
            {
                _suppressAutoHide = previous;
            }
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Keep interaction handlers suppressed while WPF materializes bound checkboxes and selections.
        // In particular, loading a saved repository list must not reset its per-link exclusions.
        InitializeWorkspaceViews();
        SelectWorkspaceTab("Dashboard");
        ApplyViewMode(_viewModel.ViewMode);
        ApplyExtraColumnVisibility();
        ApplyWindowBehavior(initialLoad: true);
        _loaded = true;
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

    private void InitializeWorkspaceViews()
    {
        _projectView = CollectionViewSource.GetDefaultView(_viewModel.Projects);
        _projectView.Filter = item =>
        {
            if (item is not ProjectEntry project || project.IsArchived) return false;
            if (!string.IsNullOrWhiteSpace(_repositorySearchText))
            {
                string haystack = string.Join(" ", project.Name, project.Note, project.GitHubFullName, project.Category, project.Subcategory, project.Language);
                if (!haystack.Contains(_repositorySearchText, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (_activeRepositoryFamilies.Count > 0)
            {
                return _activeRepositoryFamilies.Contains(project.Category);
            }

            string filter = GetModuleFilter("Repository Hub");
            if (filter == "all") return true;
            if (filter.StartsWith("family:", StringComparison.OrdinalIgnoreCase))
            {
                string family = filter["family:".Length..];
                return string.Equals(project.Category, family, StringComparison.OrdinalIgnoreCase);
            }
            if (filter.StartsWith("sub:", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = filter["sub:".Length..].Split('|', 2);
                return parts.Length == 2
                    && string.Equals(project.Category, parts[0], StringComparison.OrdinalIgnoreCase)
                    && string.Equals(project.Subcategory, parts[1], StringComparison.OrdinalIgnoreCase);
            }
            if (filter.StartsWith("repo:", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(filter["repo:".Length..], out Guid repositoryId))
            {
                return project.Id == repositoryId;
            }
            return string.Equals(project.Category, filter, StringComparison.OrdinalIgnoreCase);
        };
        ProjectsGrid.ItemsSource = _projectView;

        _portalView = CollectionViewSource.GetDefaultView(_viewModel.Portals);
        _portalView.Filter = item =>
        {
            if (item is not PortalEntry portal) return false;
            string filter = GetModuleFilter("Portals");
            return filter == "all" || string.Equals(portal.Category, filter, StringComparison.OrdinalIgnoreCase);
        };
        PortalList.ItemsSource = _portalView;

        _captureView = CollectionViewSource.GetDefaultView(_viewModel.Notes);
        _captureView.Filter = item =>
        {
            if (item is not StickyNoteEntry note) return false;

            string kindFilter = GetModuleFilter("Capture");
            if (kindFilter.Equals("archived", StringComparison.OrdinalIgnoreCase))
            {
                if (!note.IsArchived) return false;
            }
            else
            {
                if (note.IsArchived) return false;
                if (!_viewModel.IncludeCompletedCaptures && note.IsCompleted) return false;
                if (kindFilter != "all"
                    && !string.Equals(note.Kind.ToString(), kindFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return _activeCaptureSubjects.Count == 0
                || _activeCaptureSubjects.Contains(string.IsNullOrWhiteSpace(note.Subject) ? "Uncategorized" : note.Subject.Trim());
        };
        CaptureList.ItemsSource = _captureView;

        _snippetView = CollectionViewSource.GetDefaultView(_viewModel.ClipboardSnippets);
        _snippetView.Filter = item =>
        {
            if (item is not ClipboardSnippetEntry snippet) return false;
            string filter = GetModuleFilter("Clipboard");
            return filter == "all" || string.Equals(snippet.Category, filter, StringComparison.OrdinalIgnoreCase);
        };
        SnippetList.ItemsSource = _snippetView;

        _promptView = CollectionViewSource.GetDefaultView(_viewModel.PromptModules);
        _promptView.Filter = item =>
        {
            if (item is not PromptModuleEntry module) return false;
            string filter = GetModuleFilter("Prompt Builder");
            return filter == "all" || string.Equals(module.Category, filter, StringComparison.OrdinalIgnoreCase);
        };
        PromptModuleList.ItemsSource = _promptView;
    }

    private string GetModuleFilter(string module) =>
        _moduleFilters.TryGetValue(module, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "all";

    private void PrimaryNav_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string module)
        {
            SelectWorkspaceTab(module);
        }
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
                _activeModule = header;
                break;
            }
        }

        RefreshShellNavigation();
    }

    private void RefreshShellNavigation()
    {
        CurrentModuleTitle.Text = _activeModule;
        CurrentModuleSubtitle.Text = _activeModule switch
        {
            "Dashboard" => "Overview and recent work",
            "Repository Hub" => "GitHub, website, server and ChatGPT links",
            "Portals" => "Direct-open services and project sub-links",
            "Capture" => "Inbox, tasks, notes, bookmarks and transcripts",
            "Clipboard" => "One-click reusable text",
            "Prompt Builder" => "Compose reusable instruction modules",
            "Settings" => "Window and local storage behavior",
            _ => string.Empty,
        };

        QuickAddButton.Content = _activeModule switch
        {
            "Repository Hub" => "+ Project",
            "Portals" => "+ Portal",
            "Capture" => "+ Capture",
            "Clipboard" => "+ Snippet",
            "Prompt Builder" => "+ Module",
            _ => "+ Capture",
        };

        bool repositoryModule = _activeModule == "Repository Hub";
        RepoSavedListsPanel.Visibility = repositoryModule ? Visibility.Visible : Visibility.Collapsed;
        RepositoryTree.Visibility = repositoryModule ? Visibility.Visible : Visibility.Collapsed;
        SecondaryNavScroller.Visibility = repositoryModule ? Visibility.Collapsed : Visibility.Visible;
        UpdatePrimaryNavSelection();
        RefreshSecondaryNavigation();
        RefreshProjectTree();
        RefreshQuickRibbon();
        RefreshCaptureBoard();
        UpdateRepositorySelectionSummary();
        RefreshActiveView();
    }

    private void UpdatePrimaryNavSelection()
    {
        foreach (Button button in PrimaryNavPanel.Children.OfType<Button>())
        {
            bool active = string.Equals(button.Tag as string, _activeModule, StringComparison.OrdinalIgnoreCase);
            button.Background = active ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF2, 0xFF)) : Brushes.Transparent;
            button.Foreground = active ? new SolidColorBrush(Color.FromRgb(0x0B, 0x63, 0xCE)) : Brushes.Black;
        }
    }

    private void RefreshSecondaryNavigation()
    {
        _secondaryNavItems.Clear();
        string activeFilter = GetModuleFilter(_activeModule);

        void Add(string key, string label, int count) =>
            _secondaryNavItems.Add(new ShellNavItem(key, label, count, string.Equals(key, activeFilter, StringComparison.OrdinalIgnoreCase)));

        switch (_activeModule)
        {
            case "Repository Hub":
                SecondaryTitle.Text = "Projects";
                SecondaryHint.Text = "Filter the repository table";
                Add("all", "All repositories", _viewModel.Projects.Count(project => !project.IsArchived));
                foreach (IGrouping<string, ProjectEntry> group in _viewModel.Projects
                    .Where(project => !project.IsArchived)
                    .GroupBy(project => string.IsNullOrWhiteSpace(project.Category) ? "Projects" : project.Category, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Add(group.Key, group.Key, group.Count());
                }
                break;

            case "Portals":
                SecondaryTitle.Text = "Portal categories";
                SecondaryHint.Text = "Filter direct-open services";
                Add("all", "All portals", _viewModel.Portals.Count);
                foreach (IGrouping<string, PortalEntry> group in _viewModel.Portals
                    .GroupBy(portal => string.IsNullOrWhiteSpace(portal.Category) ? "General" : portal.Category, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Add(group.Key, group.Key, group.Count());
                }
                break;

            case "Capture":
                SecondaryTitle.Text = "Capture types";
                SecondaryHint.Text = "Type on the left, subject in the top ribbon";
                Func<StickyNoteEntry, bool> activeCapture = note =>
                    !note.IsArchived && (_viewModel.IncludeCompletedCaptures || !note.IsCompleted);
                Add("all", "All captures", _viewModel.Notes.Count(activeCapture));
                foreach (CaptureKind kind in Enum.GetValues<CaptureKind>())
                {
                    Add(kind.ToString(), CaptureKindLabel(kind), _viewModel.Notes.Count(note => activeCapture(note) && note.Kind == kind));
                }
                Add("archived", "Archived", _viewModel.Notes.Count(note => note.IsArchived));
                break;

            case "Clipboard":
                SecondaryTitle.Text = "Clipboard categories";
                SecondaryHint.Text = "Filter reusable text";
                Add("all", "All snippets", _viewModel.ClipboardSnippets.Count);
                foreach (IGrouping<string, ClipboardSnippetEntry> group in _viewModel.ClipboardSnippets
                    .GroupBy(snippet => string.IsNullOrWhiteSpace(snippet.Category) ? "General" : snippet.Category, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Add(group.Key, group.Key, group.Count());
                }
                break;

            case "Prompt Builder":
                SecondaryTitle.Text = "Prompt modules";
                SecondaryHint.Text = "Module categories are edited in the center";
                Add("all", "All modules", _viewModel.PromptModules.Count);
                foreach (IGrouping<string, PromptModuleEntry> group in _viewModel.PromptModules
                    .GroupBy(module => string.IsNullOrWhiteSpace(module.Category) ? "General" : module.Category, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Add(group.Key, group.Key, group.Count());
                }
                break;

            case "Settings":
                SecondaryTitle.Text = "Settings";
                SecondaryHint.Text = "Local Windows utility preferences";
                Add("all", "General", 1);
                break;

            default:
                SecondaryTitle.Text = "Overview";
                SecondaryHint.Text = "Use the modules on the left";
                Add("all", "Dashboard", 1);
                break;
        }
    }

    private void RefreshProjectTree()
    {
        _projectTreeNodes.Clear();
        int activeCount = _viewModel.Projects.Count(project => !project.IsArchived);
        _projectTreeNodes.Add(new ProjectTreeNode("all", "All repositories", activeCount, []));

        foreach (IGrouping<string, ProjectEntry> family in _viewModel.Projects
            .Where(project => !project.IsArchived)
            .GroupBy(project => string.IsNullOrWhiteSpace(project.Category) ? "Projects" : project.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            List<ProjectTreeNode> children = family
                .GroupBy(project => string.IsNullOrWhiteSpace(project.Subcategory) ? "Misc" : project.Subcategory, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ProjectTreeNode(
                    $"sub:{family.Key}|{group.Key}",
                    group.Key,
                    group.Count(),
                    group
                        .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(project => new ProjectTreeNode(
                            $"repo:{project.Id}",
                            project.Name,
                            1,
                            []))
                        .ToArray()))
                .ToList();

            _projectTreeNodes.Add(new ProjectTreeNode(
                $"family:{family.Key}",
                family.Key,
                family.Count(),
                children));
        }
    }

    private void RepositoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (!_loaded || e.NewValue is not ProjectTreeNode node)
        {
            return;
        }

        _activeRepositoryFamilies.Clear();
        _moduleFilters["Repository Hub"] = node.Key;
        RefreshQuickRibbon();
        _projectView?.Refresh();
    }

    private static string ProjectFamilyAccent(string family) => family.ToLowerInvariant() switch
    {
        "foil" => "#D99316",
        "atlas" => "#1F70D8",
        "datapass" => "#12965F",
        "fabric" => "#7B3FD0",
        "infra" => "#66758A",
        "portfolio" => "#D12C7F",
        _ => "#5B6577",
    };

    private static string CaptureKindLabel(CaptureKind kind) => kind switch
    {
        CaptureKind.QuickNote => "Quick notes",
        CaptureKind.ReadLater => "Read later",
        _ => kind.ToString(),
    };

    private void SecondaryNav_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key)
        {
            return;
        }

        _moduleFilters[_activeModule] = key;
        RefreshSecondaryNavigation();
        RefreshQuickRibbon();
        RefreshActiveView();
    }

    private void RefreshActiveView()
    {
        switch (_activeModule)
        {
            case "Repository Hub": _projectView?.Refresh(); break;
            case "Portals": _portalView?.Refresh(); break;
            case "Capture": _captureView?.Refresh(); break;
            case "Clipboard": _snippetView?.Refresh(); break;
            case "Prompt Builder": _promptView?.Refresh(); break;
        }
    }

    private void RefreshQuickRibbon()
    {
        _quickRibbonItems.Clear();
        string activeFilter = GetModuleFilter(_activeModule);

        switch (_activeModule)
        {
            case "Repository Hub":
                _quickRibbonItems.Add(new QuickRibbonItem(
                    "repo-all",
                    "all",
                    "All",
                    $"{_viewModel.Projects.Count(project => !project.IsArchived)} repos",
                    "A",
                    _activeRepositoryFamilies.Count == 0 && activeFilter == "all" ? "#0F6CBD" : "#5B6577"));

                foreach (IGrouping<string, ProjectEntry> group in _viewModel.Projects
                    .Where(project => !project.IsArchived)
                    .GroupBy(project => string.IsNullOrWhiteSpace(project.Category) ? "Projects" : project.Category, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(8))
                {
                    string glyph = group.Key.Length > 0 ? group.Key[..1].ToUpperInvariant() : "P";
                    bool selected = _activeRepositoryFamilies.Contains(group.Key);
                    _quickRibbonItems.Add(new QuickRibbonItem(
                        "repo-family",
                        group.Key,
                        group.Key,
                        $"{group.Count()} repos",
                        glyph,
                        selected ? ProjectFamilyAccent(group.Key) : "#9AA6B2"));
                }
                break;

            case "Portals":
                IEnumerable<PortalEntry> pinnedPortals = _viewModel.Portals.Where(portal => portal.IsPinnedToRibbon);
                if (!pinnedPortals.Any()) pinnedPortals = _viewModel.Portals.Take(7);
                foreach (PortalEntry portal in pinnedPortals.Take(8))
                {
                    string glyph = !string.IsNullOrWhiteSpace(portal.IconKey) ? portal.IconKey.Trim()[..1].ToUpperInvariant() : portal.Name[..Math.Min(1, portal.Name.Length)].ToUpperInvariant();
                    _quickRibbonItems.Add(new QuickRibbonItem("portal", portal.Id.ToString(), portal.Name, portal.Category, glyph, "#107C10", portal.MainUrl));
                }
                break;

            case "Capture":
                string captureFilter = GetModuleFilter("Capture");
                IEnumerable<StickyNoteEntry> captureScope = captureFilter.Equals("archived", StringComparison.OrdinalIgnoreCase)
                    ? _viewModel.Notes.Where(note => note.IsArchived)
                    : _viewModel.Notes.Where(note => !note.IsArchived && (_viewModel.IncludeCompletedCaptures || !note.IsCompleted));

                StickyNoteEntry[] captureScopeItems = captureScope.ToArray();
                _quickRibbonItems.Add(new QuickRibbonItem(
                    "capture-subject-all",
                    "all",
                    "All subjects",
                    $"{captureScopeItems.Length} items",
                    "A",
                    _activeCaptureSubjects.Count == 0 ? "#8764B8" : "#9AA6B2"));

                foreach (IGrouping<string, StickyNoteEntry> group in captureScopeItems
                    .GroupBy(note => string.IsNullOrWhiteSpace(note.Subject) ? "Uncategorized" : note.Subject.Trim(), StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(8))
                {
                    bool selected = _activeCaptureSubjects.Contains(group.Key);
                    string glyph = group.Key.Length > 0 ? group.Key[..1].ToUpperInvariant() : "S";
                    _quickRibbonItems.Add(new QuickRibbonItem(
                        "capture-subject",
                        group.Key,
                        group.Key,
                        $"{group.Count()} items",
                        glyph,
                        selected ? "#8764B8" : "#9AA6B2"));
                }
                break;

            case "Clipboard":
                IEnumerable<ClipboardSnippetEntry> pinnedSnippets = _viewModel.ClipboardSnippets.Where(snippet => snippet.IsPinned);
                if (!pinnedSnippets.Any()) pinnedSnippets = _viewModel.ClipboardSnippets.Take(7);
                foreach (ClipboardSnippetEntry snippet in pinnedSnippets.Take(8))
                {
                    string glyph = snippet.Title.Length > 0 ? snippet.Title[..1].ToUpperInvariant() : "C";
                    _quickRibbonItems.Add(new QuickRibbonItem("snippet", snippet.Id.ToString(), snippet.Title, snippet.Category, glyph, "#C239B3", snippet.Text));
                }
                break;

            case "Dashboard":
                foreach (PortalEntry portal in _viewModel.Portals.Where(portal => portal.IsPinnedToRibbon).Take(5))
                {
                    string glyph = !string.IsNullOrWhiteSpace(portal.IconKey) ? portal.IconKey.Trim()[..1].ToUpperInvariant() : portal.Name[..Math.Min(1, portal.Name.Length)].ToUpperInvariant();
                    _quickRibbonItems.Add(new QuickRibbonItem("portal", portal.Id.ToString(), portal.Name, "Portal", glyph, "#107C10", portal.MainUrl));
                }
                foreach (ClipboardSnippetEntry snippet in _viewModel.ClipboardSnippets.Where(snippet => snippet.IsPinned).Take(3))
                {
                    string glyph = snippet.Title.Length > 0 ? snippet.Title[..1].ToUpperInvariant() : "C";
                    _quickRibbonItems.Add(new QuickRibbonItem("snippet", snippet.Id.ToString(), snippet.Title, "Copy", glyph, "#C239B3", snippet.Text));
                }
                if (_quickRibbonItems.Count == 0)
                {
                    _quickRibbonItems.Add(new QuickRibbonItem("navigate", "Portals", "Portals", "Open services", "↗", "#107C10"));
                    _quickRibbonItems.Add(new QuickRibbonItem("navigate", "Repository Hub", "Repositories", "Project links", "R", "#0F6CBD"));
                    _quickRibbonItems.Add(new QuickRibbonItem("navigate", "Capture", "Capture", "Quick notes", "N", "#8764B8"));
                    _quickRibbonItems.Add(new QuickRibbonItem("navigate", "Clipboard", "Clipboard", "Reusable text", "C", "#C239B3"));
                }
                break;
        }
    }

    private void QuickRibbon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not QuickRibbonItem item)
        {
            return;
        }

        switch (item.Action)
        {
            case "repo-all":
                _activeRepositoryFamilies.Clear();
                _moduleFilters["Repository Hub"] = "all";
                RefreshProjectTree();
                RefreshQuickRibbon();
                _projectView?.Refresh();
                break;
            case "repo-family":
                _moduleFilters["Repository Hub"] = "all";
                if (!_activeRepositoryFamilies.Add(item.Key))
                {
                    _activeRepositoryFamilies.Remove(item.Key);
                }
                RefreshQuickRibbon();
                _projectView?.Refresh();
                break;
            case "capture-subject-all":
                _activeCaptureSubjects.Clear();
                RefreshQuickRibbon();
                _captureView?.Refresh();
                break;
            case "capture-subject":
                if (!_activeCaptureSubjects.Add(item.Key))
                {
                    _activeCaptureSubjects.Remove(item.Key);
                }
                RefreshQuickRibbon();
                _captureView?.Refresh();
                break;
            case "filter":
                _moduleFilters[_activeModule] = item.Key;
                RefreshSecondaryNavigation();
                RefreshQuickRibbon();
                RefreshActiveView();
                break;
            case "portal":
                OpenUrlValue(item.Value);
                break;
            case "snippet":
                CopyText(item.Value, "Pinned snippet copied");
                break;
            case "navigate":
                SelectWorkspaceTab(item.Key);
                break;
        }
    }

    private void QuickAdd_Click(object sender, RoutedEventArgs e)
    {
        switch (_activeModule)
        {
            case "Repository Hub":
                _viewModel.AddProject();
                ApplyCurrentRepositoryContext(_viewModel.Projects.Last());
                break;
            case "Portals":
                _viewModel.AddPortal();
                ApplyCurrentCategory(_viewModel.SelectedPortal, GetModuleFilter("Portals"));
                break;
            case "Clipboard":
                _viewModel.AddClipboardSnippet();
                ApplyCurrentCategory(_viewModel.SelectedSnippet, GetModuleFilter("Clipboard"));
                break;
            case "Prompt Builder":
                _viewModel.AddPromptModule();
                ApplyCurrentPromptCategory(_viewModel.PromptModules.Last());
                break;
            default:
                PrepareCaptureAddContext();
                _viewModel.AddNote();
                ApplyCurrentCaptureKind(_viewModel.SelectedNote);
                if (_activeModule == "Dashboard") SelectWorkspaceTab("Capture");
                break;
        }

        RefreshAfterDataChange();
        SafeSave();
    }

    private void ApplyCurrentRepositoryContext(ProjectEntry project)
    {
        if (_activeRepositoryFamilies.Count == 1)
        {
            project.Category = _activeRepositoryFamilies.First();
            project.Subcategory = "Misc";
            return;
        }

        string filter = GetModuleFilter("Repository Hub");
        if (filter.StartsWith("family:", StringComparison.OrdinalIgnoreCase))
        {
            project.Category = filter["family:".Length..];
            project.Subcategory = "Misc";
        }
        else if (filter.StartsWith("sub:", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = filter["sub:".Length..].Split('|', 2);
            if (parts.Length == 2)
            {
                project.Category = parts[0];
                project.Subcategory = parts[1];
            }
        }
        else if (filter.StartsWith("repo:", StringComparison.OrdinalIgnoreCase))
        {
            // A single-repository leaf is a viewing filter, not a category for new projects.
            _moduleFilters["Repository Hub"] = "all";
        }
    }

    private static void ApplyCurrentCategory(PortalEntry? item, string filter)
    {
        if (item is not null && filter != "all")
        {
            item.Category = filter;
        }
    }

    private static void ApplyCurrentCategory(ClipboardSnippetEntry? item, string filter)
    {
        if (item is not null && filter != "all")
        {
            item.Category = filter;
        }
    }

    private void ApplyCurrentCaptureKind(StickyNoteEntry? note)
    {
        if (note is null)
        {
            return;
        }

        string filter = GetModuleFilter("Capture");
        if (filter != "all" && Enum.TryParse(filter, ignoreCase: true, out CaptureKind kind))
        {
            note.Kind = kind;
        }

        if (_activeCaptureSubjects.Count == 1)
        {
            string subject = _activeCaptureSubjects.First();
            note.Subject = subject.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase) ? string.Empty : subject;
        }
    }

    private void RefreshCaptureBoard()
    {
        _captureBoardSections.Clear();
        foreach (CaptureKind kind in Enum.GetValues<CaptureKind>())
        {
            StickyNoteEntry[] items = _viewModel.Notes
                .Where(note => !note.IsArchived
                    && (_viewModel.IncludeCompletedCaptures || !note.IsCompleted)
                    && note.Kind == kind)
                .OrderByDescending(note => note.IsPinned)
                .ThenByDescending(note => note.UpdatedUtc)
                .Take(3)
                .ToArray();

            int count = _viewModel.Notes.Count(note =>
                !note.IsArchived
                && (_viewModel.IncludeCompletedCaptures || !note.IsCompleted)
                && note.Kind == kind);
            _captureBoardSections.Add(new CaptureBoardSection(kind.ToString(), CaptureKindLabel(kind), count, items));
        }
    }

    private void DashboardCaptureKind_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key)
        {
            return;
        }

        _moduleFilters["Capture"] = key;
        SelectWorkspaceTab("Capture");
    }

    private void RefreshAfterDataChange()
    {
        _projectView?.Refresh();
        _portalView?.Refresh();
        _captureView?.Refresh();
        _snippetView?.Refresh();
        _promptView?.Refresh();
        RefreshSecondaryNavigation();
        RefreshProjectTree();
        RefreshQuickRibbon();
        RefreshCaptureBoard();
        UpdateRepositorySelectionSummary();
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
            PowerOpsShell.Visibility = Visibility.Collapsed;
            Width = 390;
            Height = Math.Max(Height, 700);
            return;
        }

        SidebarPanel.Visibility = Visibility.Collapsed;
        PowerOpsShell.Visibility = Visibility.Visible;

        if (mode == WorkspaceViewMode.Compact)
        {
            PrimaryNavColumn.Width = new GridLength(155);
            SecondaryNavColumn.Width = new GridLength(0);
            Width = 1040;
            Height = 760;
        }
        else
        {
            PrimaryNavColumn.Width = new GridLength(185);
            SecondaryNavColumn.Width = new GridLength(220);
            Width = 1480;
            Height = 900;
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
        ApplyCurrentRepositoryContext(_viewModel.Projects.Last());
        RefreshAfterDataChange();
        SafeSave();
    }

    private async void SyncGitHub_Click(object sender, RoutedEventArgs e)
    {
        Button? button = sender as Button;
        if (button is not null) button.IsEnabled = false;
        _viewModel.StatusText = "Syncing GitHub repositories…";

        try
        {
            GitHubRepositoryFetchResult fetched = await GitHubRepositorySyncService.FetchAsync(_viewModel.GitHubOwner);
            RepositoryMergeSummary merged = _viewModel.MergeGitHubRepositories(fetched.Repositories);
            _moduleFilters["Repository Hub"] = "all";
            RefreshAfterDataChange();
            ProjectsGrid.Items.Refresh();
            SafeSave();
            _viewModel.StatusText = $"{fetched.Source}: {fetched.Repositories.Count} found · {merged.Added} added · {merged.Updated} updated";
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(ex.Message, "GitHub sync failed", MessageBoxImage.Warning);
            _viewModel.StatusText = "GitHub sync failed";
        }
        finally
        {
            if (button is not null) button.IsEnabled = true;
        }
    }

    private void AddPortal_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddPortal();
        ApplyCurrentCategory(_viewModel.SelectedPortal, GetModuleFilter("Portals"));
        RefreshAfterDataChange();
        SafeSave();
    }

    private void Category_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        switch ((sender as FrameworkElement)?.DataContext)
        {
            case PortalEntry:
                _portalView?.Refresh();
                break;
            case ClipboardSnippetEntry:
                _snippetView?.Refresh();
                break;
            case PromptModuleEntry:
                _promptView?.Refresh();
                break;
        }

        RefreshSecondaryNavigation();
        RefreshQuickRibbon();
        SafeSave();
    }

    private void PortalPinChanged_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        RefreshQuickRibbon();
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
        RefreshAfterDataChange();
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

    private void DeletePortalLink_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPortal is not PortalEntry portal
            || (sender as FrameworkElement)?.Tag is not PortalLinkEntry link)
        {
            return;
        }

        portal.Links.Remove(link);
        portal.UpdatedUtc = DateTimeOffset.UtcNow;
        PortalLinksGrid.Items.Refresh();
        _viewModel.StatusText = "Portal sub-link removed";
        SafeSave();
    }

    private void AddSnippet_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddClipboardSnippet();
        ApplyCurrentCategory(_viewModel.SelectedSnippet, GetModuleFilter("Clipboard"));
        RefreshAfterDataChange();
        SafeSave();
    }

    private void SnippetPinChanged_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        RefreshQuickRibbon();
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
        RefreshAfterDataChange();
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
            RefreshAfterDataChange();
            SafeSave();
        }
    }

    private void RepoSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _repositorySearchText = RepoSearchBox.Text.Trim();
        _projectView?.Refresh();
    }

    private void ProjectsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            RefreshProjectTree();
            RefreshQuickRibbon();
            _projectView?.Refresh();
            SafeSave();
        }));
    }

    private void ProjectIncludeChanged_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshingProjectSelection
            || (sender as FrameworkElement)?.DataContext is not ProjectEntry project
            || !_loaded)
        {
            return;
        }

        if (project.IncludeInCopyAll)
        {
            project.CopyRepo = !string.IsNullOrWhiteSpace(project.RepoUrl);
            project.CopySite = !string.IsNullOrWhiteSpace(project.SiteUrl);
            project.CopyServer = !string.IsNullOrWhiteSpace(project.ServerUrl);
            project.CopyChatGpt = !string.IsNullOrWhiteSpace(project.ChatGptUrl);
        }

        RefreshProjectGridSelection();
        UpdateRepositorySelectionSummary();
        SafeSave();
    }

    private void LinkIncludeChanged_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshingProjectSelection
            || (sender as CheckBox)?.DataContext is not ProjectEntry project
            || !_loaded)
        {
            return;
        }

        if ((sender as CheckBox)?.IsChecked == true && !project.IncludeInCopyAll)
        {
            project.IncludeInCopyAll = true;
            RefreshProjectGridSelection();
        }

        UpdateRepositorySelectionSummary();
        SafeSave();
    }

    private void UpdateRepositorySelectionSummary()
    {
        int selected = _viewModel.Projects.Count(project => project.IncludeInCopyAll && !project.IsArchived);
        SelectedRepoCountText.Text = $"{selected} selected";
    }

    private void RefreshProjectGridSelection()
    {
        if (_refreshingProjectSelection)
        {
            return;
        }

        _refreshingProjectSelection = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try
            {
                ProjectsGrid.Items.Refresh();
            }
            finally
            {
                _refreshingProjectSelection = false;
            }
        }));
    }

    private void SaveRepoList_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Projects.Any(project => project.IncludeInCopyAll && !project.IsArchived))
        {
            _viewModel.StatusText = "Select at least one repository row first";
            return;
        }

        _viewModel.SaveRepositoryList(NewRepoListName.Text);
        NewRepoListName.Clear();
        SafeSave();
    }

    private void LoadRepoList_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RepositoryListEntry list)
        {
            return;
        }

        _viewModel.ApplyRepositoryList(list);
        ProjectsGrid.Items.Refresh();
        _activeRepositoryFamilies.Clear();
        _moduleFilters["Repository Hub"] = "all";
        SelectWorkspaceTab("Repository Hub");
        SafeSave();
    }

    private void CopyRepoList_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is RepositoryListEntry list)
        {
            CopyText(_viewModel.FormatRepositoryList(list), $"Copied saved list '{list.Name}'");
        }
    }

    private void DeleteRepoList_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RepositoryListEntry list)
        {
            return;
        }

        _viewModel.RemoveRepositoryList(list);
        SafeSave();
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
        CopyText(ProjectClipboardFormatter.FormatAll(_viewModel.Projects), "Selected repository URLs copied");

    private void CopyGithubColumn_Click(object sender, RoutedEventArgs e) =>
        CopyProjectLinkColumn(project => project.RepoUrl, project => project.CopyRepo, "GitHub URLs");

    private void CopyWebsiteColumn_Click(object sender, RoutedEventArgs e) =>
        CopyProjectLinkColumn(project => project.SiteUrl, project => project.CopySite, "website URLs");

    private void CopyServerColumn_Click(object sender, RoutedEventArgs e) =>
        CopyProjectLinkColumn(project => project.ServerUrl, project => project.CopyServer, "server URLs");

    private void CopyChatGptColumn_Click(object sender, RoutedEventArgs e) =>
        CopyProjectLinkColumn(project => project.ChatGptUrl, project => project.CopyChatGpt, "ChatGPT URLs");

    private void CopyProjectLinkColumn(
        Func<ProjectEntry, string> valueSelector,
        Func<ProjectEntry, bool> includeSelector,
        string label)
    {
        string[] values = _viewModel.Projects
            .Where(project => project.IncludeInCopyAll && !project.IsArchived && includeSelector(project))
            .Select(valueSelector)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        CopyText(string.Join(Environment.NewLine, values), $"{values.Length} {label} copied");
    }

    private void OpenSelectedRepos_Click(object sender, RoutedEventArgs e)
    {
        string[] urls = _viewModel.Projects
            .Where(project => project.IncludeInCopyAll && !project.IsArchived && !string.IsNullOrWhiteSpace(project.RepoUrl))
            .Select(project => project.RepoUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        if (urls.Length == 0)
        {
            _viewModel.StatusText = "No repository rows selected";
            return;
        }

        foreach (string url in urls)
        {
            if (UrlNormalizer.TryNormalizeOptionalWebUrl(url, out string normalized) && !string.IsNullOrWhiteSpace(normalized))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(normalized) { UseShellExecute = true });
                }
                catch
                {
                    // Continue opening the remaining selected repositories.
                }
            }
        }

        _viewModel.StatusText = urls.Length == 20
            ? "Opened the first 20 selected repositories"
            : $"Opened {urls.Length} selected repositories";
    }

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        string? url = (sender as FrameworkElement)?.Tag as string;
        OpenUrlValue(url);
    }

    private void OpenUrlValue(string? url)
    {
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

        try
        {
            Process.Start(new ProcessStartInfo(normalized) { UseShellExecute = true });
            _viewModel.StatusText = "Opened link";
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(ex.Message, "Unable to open link", MessageBoxImage.Warning);
        }
    }

    private void AddModule_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddPromptModule();
        ApplyCurrentPromptCategory(_viewModel.PromptModules.Last());
        _promptView?.Refresh();
        RefreshSecondaryNavigation();
        SafeSave();
    }

    private void ApplyCurrentPromptCategory(PromptModuleEntry module)
    {
        string filter = GetModuleFilter("Prompt Builder");
        if (filter != "all")
        {
            module.Category = filter;
        }
    }

    private void MoveModuleUp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PromptModuleEntry module)
        {
            _viewModel.MoveModule(module, -1);
            SafeSave();
        }
    }

    private void MoveModuleDown_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PromptModuleEntry module)
        {
            _viewModel.MoveModule(module, 1);
            SafeSave();
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

    private void RecallRecentPrompt_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RecentPromptEntry recent)
        {
            return;
        }

        _viewModel.PromptPreview = recent.Text;
        _viewModel.StatusText = "Recent prompt recalled";
    }

    private void CopyRecentPrompt_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is RecentPromptEntry recent)
        {
            CopyText(recent.Text, "Recent prompt copied");
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
        PrepareCaptureAddContext();
        _viewModel.AddNote();
        ApplyCurrentCaptureKind(_viewModel.SelectedNote);
        RefreshAfterDataChange();
        SafeSave();
    }

    private void PrepareCaptureAddContext()
    {
        if (GetModuleFilter("Capture").Equals("archived", StringComparison.OrdinalIgnoreCase))
        {
            _moduleFilters["Capture"] = "all";
            _activeCaptureSubjects.Clear();
        }
    }

    private void CaptureItemStateChanged_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded || (sender as FrameworkElement)?.DataContext is not StickyNoteEntry note)
        {
            return;
        }

        note.UpdatedUtc = DateTimeOffset.UtcNow;
        _captureView?.Refresh();
        RefreshSecondaryNavigation();
        RefreshQuickRibbon();
        RefreshCaptureBoard();
        SafeSave();
    }

    private void CaptureCompletionFilterChanged_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        _captureView?.Refresh();
        RefreshSecondaryNavigation();
        RefreshQuickRibbon();
        RefreshCaptureBoard();
        SafeSave();
    }

    private void CaptureDueDateChanged_Click(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        SafeSave();
    }

    private void CaptureSubject_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        RefreshSecondaryNavigation();
        RefreshQuickRibbon();
        _captureView?.Refresh();
        SafeSave();
    }

    private void CaptureKindChanged_Click(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _captureView?.Refresh();
        RefreshSecondaryNavigation();
        RefreshQuickRibbon();
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

    private void ExportCaptures_Click(object sender, RoutedEventArgs e)
    {
        StickyNoteEntry[] captures = _viewModel.Notes
            .Where(note => !note.IsArchived)
            .OrderBy(note => note.Kind)
            .ThenByDescending(note => note.IsPinned)
            .ThenByDescending(note => note.UpdatedUtc)
            .ToArray();

        if (captures.Length == 0)
        {
            _viewModel.StatusText = "No active captures to export";
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "JSON files (*.json)|*.json|Markdown files (*.md)|*.md",
            FileName = "power-ops-captures.json",
        };

        _suppressAutoHide = true;
        try
        {
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            if (string.Equals(Path.GetExtension(dialog.FileName), ".md", StringComparison.OrdinalIgnoreCase))
            {
                List<string> lines = ["# Power Ops captures", "", $"Exported: {DateTimeOffset.Now:O}", ""];
                foreach (StickyNoteEntry note in captures)
                {
                    lines.AddRange(FormatCaptureMarkdown(note));
                    lines.Add("");
                }
                File.WriteAllText(dialog.FileName, string.Join(Environment.NewLine, lines));
            }
            else
            {
                var envelope = new
                {
                    schemaVersion = 1,
                    exportedUtc = DateTimeOffset.UtcNow,
                    captures,
                };
                File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true }));
            }

            _viewModel.StatusText = $"Exported {captures.Length} captures";
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
                File.WriteAllText(dialog.FileName, string.Join(Environment.NewLine, FormatCaptureMarkdown(note)));
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

    private static IReadOnlyList<string> FormatCaptureMarkdown(StickyNoteEntry note)
    {
        List<string> lines =
        [
            "## " + (string.IsNullOrWhiteSpace(note.Title) ? "Capture" : note.Title.Trim()),
            "",
            $"- Type: {note.Kind}",
        ];
        if (!string.IsNullOrWhiteSpace(note.Subject)) lines.Add("- Subject: " + note.Subject.Trim());
        if (!string.IsNullOrWhiteSpace(note.Status)) lines.Add("- Status: " + note.Status.Trim());
        if (!string.IsNullOrWhiteSpace(note.Priority)) lines.Add("- Priority: " + note.Priority.Trim());
        if (note.DueUtc is not null) lines.Add("- Due: " + note.DueUtc.Value.ToString("O"));
        if (!string.IsNullOrWhiteSpace(note.Url)) lines.Add("- URL: " + note.Url.Trim());
        if (!string.IsNullOrWhiteSpace(note.Labels)) lines.Add("- Labels: " + note.Labels.Trim());
        lines.Add("");
        lines.Add(note.Text ?? string.Empty);
        return lines;
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

    private void DeleteCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNote is not StickyNoteEntry note)
        {
            _viewModel.StatusText = "No capture selected";
            return;
        }

        string title = string.IsNullOrWhiteSpace(note.Title) ? "this capture" : $"'{note.Title.Trim()}'";
        MessageBoxResult result = MessageBox.Show(
            this,
            $"Delete {title} permanently?\n\nArchive is safer if you may need it later.",
            "Delete capture",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _viewModel.RemoveNote(note);
        RefreshAfterDataChange();
        SafeSave();
    }

    private void ArchiveNote_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNote is StickyNoteEntry note)
        {
            _viewModel.ArchiveOrRestoreNote(note);
            RefreshAfterDataChange();
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
                bool wasLoaded = _loaded;
                _loaded = false;
                try
                {
                    _viewModel.Import(dialog.FileName);
                    _moduleFilters.Clear();
                    _activeRepositoryFamilies.Clear();
                    _activeCaptureSubjects.Clear();
                    _repositorySearchText = string.Empty;
                    RepoSearchBox.Clear();
                    InitializeWorkspaceViews();
                    ApplyViewMode(_viewModel.ViewMode);
                    ApplyWindowBehavior();
                    ApplyExtraColumnVisibility();
                }
                finally
                {
                    _loaded = wasLoaded;
                }

                RefreshAfterDataChange();
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

        try
        {
            Clipboard.SetText(text);
            _viewModel.StatusText = successStatus;
            return true;
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "Clipboard copy failed";
            ShowOwnedMessage(ex.Message, "Clipboard unavailable", MessageBoxImage.Warning);
            return false;
        }
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
