using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
public sealed record CaptureExportEntry(
    Guid Id,
    CaptureKind Kind,
    string Title,
    string Subject,
    string Text,
    string Url,
    string Labels,
    string Status,
    string Priority,
    DateTimeOffset? DueUtc,
    Guid? ProjectId,
    string Project,
    bool IsPinned,
    bool IsCompleted,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public partial class MainWindow : Window
{
    private static readonly string[] CommonResourceProviders =
    [
        "GitHub",
        "Google Drive",
        "Dropbox",
        "OneDrive",
        "Notion",
        "SharePoint",
    ];
    private static readonly JsonSerializerOptions CaptureExportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

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
    private readonly Dictionary<string, string> _moduleSearchTerms = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeRepositoryFamilies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeResourceProviders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeCaptureSubjects = new(StringComparer.OrdinalIgnoreCase);
    private ICollectionView? _projectView;
    private ICollectionView? _portalView;
    private ICollectionView? _resourceView;
    private ICollectionView? _captureView;
    private ICollectionView? _sidebarCaptureView;
    private ICollectionView? _sidebarResourceView;
    private ICollectionView? _snippetView;
    private ICollectionView? _promptView;
    private string _activeModule = "Dashboard";
    private string _repositorySearchText = string.Empty;
    private bool _suppressShellSearchChange;

    public MainWindow()
        : this(new WorkspaceStore())
    {
    }

    internal MainWindow(WorkspaceStore store)
    {
        InitializeComponent();
        _viewModel = new MainViewModel(store);
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
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            CaptureCurrentWindowPlacement();
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
        SelectWorkspaceTab(NormalizeModule(_viewModel.LastModule));
        ApplyViewMode(_viewModel.ViewMode);
        ApplyExtraColumnVisibility();
        ApplyWindowBehavior(initialLoad: true);
        _loaded = true;
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        bool saveSucceeded = true;
        if (_loaded && !_suppressAutoHide)
        {
            CaptureCurrentWindowPlacement();
            saveSucceeded = SafeSave();
        }

        if (!saveSucceeded
            || _suppressAutoHide
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
            if (item is not ProjectEntry project) return false;
            string repositoryFilter = GetModuleFilter("Repository Hub");
            bool archivedView = repositoryFilter.Equals("archived", StringComparison.OrdinalIgnoreCase);
            if (archivedView)
            {
                if (!project.IsArchived) return false;
            }
            else if (project.IsArchived)
            {
                return false;
            }

            string shellSearch = GetModuleSearch("Repository Hub");
            if (!string.IsNullOrWhiteSpace(shellSearch))
            {
                string shellHaystack = string.Join(" ", project.Name, project.Note, project.GitHubFullName, project.Category, project.Subcategory, project.Language, project.RepoUrl, project.SiteUrl, project.ServerUrl, project.ChatGptUrl);
                if (!shellHaystack.Contains(shellSearch, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
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

            string filter = repositoryFilter;
            if (filter == "archived") return true;
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
        _projectView.SortDescriptions.Clear();
        _projectView.SortDescriptions.Add(new SortDescription(nameof(ProjectEntry.Category), ListSortDirection.Ascending));
        _projectView.SortDescriptions.Add(new SortDescription(nameof(ProjectEntry.Subcategory), ListSortDirection.Ascending));
        _projectView.SortDescriptions.Add(new SortDescription(nameof(ProjectEntry.Name), ListSortDirection.Ascending));
        ProjectsGrid.ItemsSource = _projectView;

        _portalView = CollectionViewSource.GetDefaultView(_viewModel.Portals);
        _portalView.Filter = item =>
        {
            if (item is not PortalEntry portal) return false;
            string shellSearch = GetModuleSearch("Portals");
            if (!string.IsNullOrWhiteSpace(shellSearch))
            {
                string links = string.Join(" ", (portal.Links ?? []).Select(link => $"{link.Label} {link.Project} {link.Url} {link.Note}"));
                string haystack = string.Join(" ", portal.Name, portal.Category, portal.MainUrl, links);
                if (!haystack.Contains(shellSearch, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            string filter = GetModuleFilter("Portals");
            if (filter == "all") return true;
            if (filter.Equals("favorites", StringComparison.OrdinalIgnoreCase)) return portal.IsFavorite;
            if (filter.Equals("pinned", StringComparison.OrdinalIgnoreCase)) return portal.IsPinnedToRibbon;
            return string.Equals(portal.Category, filter, StringComparison.OrdinalIgnoreCase);
        };
        _portalView.SortDescriptions.Clear();
        _portalView.SortDescriptions.Add(new SortDescription(nameof(PortalEntry.IsFavorite), ListSortDirection.Descending));
        _portalView.SortDescriptions.Add(new SortDescription(nameof(PortalEntry.IsPinnedToRibbon), ListSortDirection.Descending));
        _portalView.SortDescriptions.Add(new SortDescription(nameof(PortalEntry.SortOrder), ListSortDirection.Ascending));
        _portalView.SortDescriptions.Add(new SortDescription(nameof(PortalEntry.Name), ListSortDirection.Ascending));
        PortalList.ItemsSource = _portalView;

        _resourceView = CollectionViewSource.GetDefaultView(_viewModel.Resources);
        _resourceView.Filter = item =>
            item is WorkspaceResourceEntry resource
            && ResourceCatalogService.MatchesFilter(
                resource,
                _activeResourceProviders,
                GetModuleFilter("Resources"),
                GetModuleSearch("Resources"));
        _resourceView.SortDescriptions.Clear();
        _resourceView.SortDescriptions.Add(new SortDescription(nameof(WorkspaceResourceEntry.IsFavorite), ListSortDirection.Descending));
        _resourceView.SortDescriptions.Add(new SortDescription(nameof(WorkspaceResourceEntry.IsPinned), ListSortDirection.Descending));
        _resourceView.SortDescriptions.Add(new SortDescription(nameof(WorkspaceResourceEntry.SortOrder), ListSortDirection.Ascending));
        _resourceView.SortDescriptions.Add(new SortDescription(nameof(WorkspaceResourceEntry.Name), ListSortDirection.Ascending));
        ResourceList.ItemsSource = _resourceView;

        _sidebarResourceView = new ListCollectionView((IList)_viewModel.Resources)
        {
            Filter = item => item is WorkspaceResourceEntry resource && (resource.IsPinned || resource.IsFavorite),
        };
        _sidebarResourceView.SortDescriptions.Add(new SortDescription(nameof(WorkspaceResourceEntry.IsPinned), ListSortDirection.Descending));
        _sidebarResourceView.SortDescriptions.Add(new SortDescription(nameof(WorkspaceResourceEntry.IsFavorite), ListSortDirection.Descending));
        _sidebarResourceView.SortDescriptions.Add(new SortDescription(nameof(WorkspaceResourceEntry.SortOrder), ListSortDirection.Ascending));
        SidebarResourceList.ItemsSource = _sidebarResourceView;
        DashboardResourceList.ItemsSource = _sidebarResourceView;

        _captureView = CollectionViewSource.GetDefaultView(_viewModel.Notes);
        _captureView.Filter = item =>
        {
            if (item is not StickyNoteEntry note) return false;

            string shellSearch = GetModuleSearch("Capture");
            if (!string.IsNullOrWhiteSpace(shellSearch))
            {
                string haystack = string.Join(" ", note.Title, note.Subject, note.Text, note.Url, note.Labels, note.Status, note.Priority, note.Kind);
                if (!haystack.Contains(shellSearch, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

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
        _captureView.SortDescriptions.Clear();
        _captureView.SortDescriptions.Add(new SortDescription(nameof(StickyNoteEntry.IsPinned), ListSortDirection.Descending));
        _captureView.SortDescriptions.Add(new SortDescription(nameof(StickyNoteEntry.UpdatedUtc), ListSortDirection.Descending));
        CaptureList.ItemsSource = _captureView;

        _sidebarCaptureView = new ListCollectionView((IList)_viewModel.Notes)
        {
            Filter = item => item is StickyNoteEntry note
                && !note.IsArchived
                && (_viewModel.IncludeCompletedCaptures || !note.IsCompleted),
        };
        SidebarCaptureList.ItemsSource = _sidebarCaptureView;

        _snippetView = CollectionViewSource.GetDefaultView(_viewModel.ClipboardSnippets);
        _snippetView.Filter = item =>
        {
            if (item is not ClipboardSnippetEntry snippet) return false;
            string shellSearch = GetModuleSearch("Clipboard");
            if (!string.IsNullOrWhiteSpace(shellSearch))
            {
                string haystack = string.Join(" ", snippet.Title, snippet.Category, snippet.Text, snippet.Tags);
                if (!haystack.Contains(shellSearch, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            string filter = GetModuleFilter("Clipboard");
            if (filter == "all") return true;
            if (filter.Equals("pinned", StringComparison.OrdinalIgnoreCase)) return snippet.IsPinned;
            return string.Equals(snippet.Category, filter, StringComparison.OrdinalIgnoreCase);
        };
        _snippetView.SortDescriptions.Clear();
        _snippetView.SortDescriptions.Add(new SortDescription(nameof(ClipboardSnippetEntry.IsPinned), ListSortDirection.Descending));
        _snippetView.SortDescriptions.Add(new SortDescription(nameof(ClipboardSnippetEntry.SortOrder), ListSortDirection.Ascending));
        _snippetView.SortDescriptions.Add(new SortDescription(nameof(ClipboardSnippetEntry.Title), ListSortDirection.Ascending));
        SnippetList.ItemsSource = _snippetView;

        _promptView = CollectionViewSource.GetDefaultView(_viewModel.PromptModules);
        _promptView.Filter = item =>
        {
            if (item is not PromptModuleEntry module) return false;
            string shellSearch = GetModuleSearch("Prompt Builder");
            if (!string.IsNullOrWhiteSpace(shellSearch))
            {
                string haystack = string.Join(" ", module.Title, module.Category, module.Body);
                if (!haystack.Contains(shellSearch, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            string filter = GetModuleFilter("Prompt Builder");
            return filter == "all" || string.Equals(module.Category, filter, StringComparison.OrdinalIgnoreCase);
        };
        _promptView.SortDescriptions.Clear();
        _promptView.SortDescriptions.Add(new SortDescription(nameof(PromptModuleEntry.SortOrder), ListSortDirection.Ascending));
        _promptView.SortDescriptions.Add(new SortDescription(nameof(PromptModuleEntry.Title), ListSortDirection.Ascending));
        PromptModuleList.ItemsSource = _promptView;
    }

    private string GetModuleFilter(string module) =>
        _moduleFilters.TryGetValue(module, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "all";

    private string GetModuleSearch(string module) =>
        _moduleSearchTerms.TryGetValue(module, out string? value) ? value : string.Empty;

    private void ShellSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressShellSearchChange || !_loaded)
        {
            return;
        }

        _moduleSearchTerms[_activeModule] = ShellSearchBox.Text.Trim();
        RefreshActiveView();
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        System.Windows.Input.ModifierKeys modifiers = System.Windows.Input.Keyboard.Modifiers;
        bool searchAvailable =
            PowerOpsShell.IsVisible
            && ShellSearchBox.IsVisible
            && ShellSearchBox.IsEnabled;

        if (e.Key == System.Windows.Input.Key.K
            && ShellKeyboardPolicy.ShouldFocusSearch(
                control: (modifiers & System.Windows.Input.ModifierKeys.Control) != 0,
                shift: (modifiers & System.Windows.Input.ModifierKeys.Shift) != 0,
                alt: (modifiers & System.Windows.Input.ModifierKeys.Alt) != 0,
                windows: (modifiers & System.Windows.Input.ModifierKeys.Windows) != 0,
                searchAvailable))
        {
            e.Handled = true;
            ShellSearchBox.Focus();
            ShellSearchBox.SelectAll();
            return;
        }

        if (ShellKeyboardPolicy.ShouldClearSearch(
                escapePressed: e.Key == System.Windows.Input.Key.Escape,
                noModifiers: modifiers == System.Windows.Input.ModifierKeys.None,
                searchFocused: ShellSearchBox.IsKeyboardFocusWithin,
                hasSearchText: !string.IsNullOrEmpty(ShellSearchBox.Text)))
        {
            e.Handled = true;
            ShellSearchBox.Clear();
        }
    }

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
                _viewModel.LastModule = header;
                break;
            }
        }

        _suppressShellSearchChange = true;
        try
        {
            ShellSearchBox.Text = GetModuleSearch(_activeModule);
        }
        finally
        {
            _suppressShellSearchChange = false;
        }

        RefreshShellNavigation();
    }

    private static string NormalizeModule(string? module)
    {
        string normalized = string.IsNullOrWhiteSpace(module) ? "Dashboard" : module.Trim();
        return normalized is "Dashboard" or "Repository Hub" or "Portals" or "Resources" or "Capture" or "Clipboard" or "Prompt Builder" or "Settings"
            ? normalized
            : "Dashboard";
    }

    private void RefreshShellNavigation()
    {
        CurrentModuleTitle.Text = _activeModule switch
        {
            "Portals" => "Portal Launcher",
            "Resources" => "Resource Hub",
            _ => _activeModule,
        };
        CurrentModuleSubtitle.Text = _activeModule switch
        {
            "Dashboard" => "Overview and recent work",
            "Repository Hub" => "GitHub, website, server and ChatGPT links",
            "Portals" => "Direct-open services and project sub-links",
            "Resources" => "Exact folders, repositories, documents and dashboards",
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
            "Resources" => "+ Resource",
            "Capture" => "+ Capture",
            "Clipboard" => "+ Snippet",
            "Prompt Builder" => "+ Module",
            _ => "+ Capture",
        };

        bool searchableModule = _activeModule is "Repository Hub" or "Portals" or "Resources" or "Capture" or "Clipboard" or "Prompt Builder";
        ShellSearchBox.IsEnabled = searchableModule;
        ShellSearchBox.Opacity = searchableModule ? 1.0 : 0.45;

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
                Add("favorites", "Favorites", _viewModel.Portals.Count(portal => portal.IsFavorite));
                Add("pinned", "Pinned to ribbon", _viewModel.Portals.Count(portal => portal.IsPinnedToRibbon));
                foreach (IGrouping<string, PortalEntry> group in _viewModel.Portals
                    .GroupBy(portal => string.IsNullOrWhiteSpace(portal.Category) ? "General" : portal.Category, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Add(group.Key, group.Key, group.Count());
                }
                break;

            case "Resources":
                SecondaryTitle.Text = "Resource groups";
                SecondaryHint.Text = "Provider on top; project/group on the left";
                Add("all", "All resources", _viewModel.Resources.Count);
                Add("favorites", "Favorites", _viewModel.Resources.Count(resource => resource.IsFavorite));
                Add("pinned", "Pinned to ribbon", _viewModel.Resources.Count(resource => resource.IsPinned));
                foreach (IGrouping<string, WorkspaceResourceEntry> group in _viewModel.Resources
                    .GroupBy(resource => string.IsNullOrWhiteSpace(resource.Group) ? "General" : resource.Group, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Add($"group:{group.Key}", group.Key, group.Count());
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
                Add("pinned", "Pinned to ribbon", _viewModel.ClipboardSnippets.Count(snippet => snippet.IsPinned));
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
        int archivedCount = _viewModel.Projects.Count(project => project.IsArchived);
        _projectTreeNodes.Add(new ProjectTreeNode("all", "All repositories", activeCount, []));
        if (archivedCount > 0)
        {
            _projectTreeNodes.Add(new ProjectTreeNode("archived", "Archived", archivedCount, []));
        }

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

    private static string FirstGlyph(string? value, string fallback = "•")
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return StringInfo.GetNextTextElement(normalized).ToUpperInvariant();
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

    private static string ResourceProviderGlyph(string provider) => provider.ToLowerInvariant() switch
    {
        "github" => "GH",
        "google drive" => "GD",
        "dropbox" => "DB",
        "onedrive" => "OD",
        "notion" => "N",
        "sharepoint" => "SP",
        _ => FirstGlyph(provider, "R"),
    };

    private static string ResourceProviderAccent(string provider) => provider.ToLowerInvariant() switch
    {
        "github" => "#24292F",
        "google drive" => "#0F9D58",
        "dropbox" => "#0061FF",
        "onedrive" => "#0078D4",
        "sharepoint" => "#038387",
        "notion" => "#404040",
        _ => "#5B5FC7",
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
            case "Resources": _resourceView?.Refresh(); break;
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
                    string glyph = FirstGlyph(!string.IsNullOrWhiteSpace(portal.IconKey) ? portal.IconKey : portal.Name);
                    _quickRibbonItems.Add(new QuickRibbonItem("portal", portal.Id.ToString(), portal.Name, portal.Category, glyph, "#107C10", portal.MainUrl));
                }
                break;

            case "Resources":
                _quickRibbonItems.Add(new QuickRibbonItem(
                    "resource-all",
                    "all",
                    "All",
                    $"{_viewModel.Resources.Count} links",
                    "A",
                    _activeResourceProviders.Count == 0 && activeFilter == "all" ? "#5B5FC7" : "#9AA6B2"));

                Dictionary<string, int> providerCounts = _viewModel.Resources
                    .GroupBy(resource => string.IsNullOrWhiteSpace(resource.Provider) ? "Other" : resource.Provider, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

                IEnumerable<string> resourceProviders = CommonResourceProviders
                    .Concat(providerCounts.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8);

                foreach (string provider in resourceProviders)
                {
                    int count = providerCounts.TryGetValue(provider, out int providerCount) ? providerCount : 0;
                    bool selected = _activeResourceProviders.Contains(provider);
                    _quickRibbonItems.Add(new QuickRibbonItem(
                        "resource-provider",
                        provider,
                        provider,
                        $"{count} links",
                        ResourceProviderGlyph(provider),
                        selected ? ResourceProviderAccent(provider) : "#9AA6B2"));
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
                    string glyph = FirstGlyph(snippet.Title, "C");
                    _quickRibbonItems.Add(new QuickRibbonItem("snippet", snippet.Id.ToString(), snippet.Title, snippet.Category, glyph, "#C239B3", snippet.Text));
                }
                break;

            case "Dashboard":
                foreach (PortalEntry portal in _viewModel.Portals.Where(portal => portal.IsPinnedToRibbon).Take(5))
                {
                    string glyph = FirstGlyph(!string.IsNullOrWhiteSpace(portal.IconKey) ? portal.IconKey : portal.Name);
                    _quickRibbonItems.Add(new QuickRibbonItem("portal", portal.Id.ToString(), portal.Name, "Portal", glyph, "#107C10", portal.MainUrl));
                }
                foreach (WorkspaceResourceEntry resource in _viewModel.Resources.Where(resource => resource.IsPinned).Take(3))
                {
                    string glyph = FirstGlyph(resource.Provider, "R");
                    _quickRibbonItems.Add(new QuickRibbonItem("resource", resource.Id.ToString(), resource.Name, resource.Provider, glyph, "#5B5FC7", resource.Url));
                }
                foreach (ClipboardSnippetEntry snippet in _viewModel.ClipboardSnippets.Where(snippet => snippet.IsPinned).Take(3))
                {
                    string glyph = FirstGlyph(snippet.Title, "C");
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
            case "resource-all":
                _activeResourceProviders.Clear();
                RefreshQuickRibbon();
                _resourceView?.Refresh();
                break;
            case "resource-provider":
                if (!_activeResourceProviders.Add(item.Key))
                {
                    _activeResourceProviders.Remove(item.Key);
                }
                RefreshQuickRibbon();
                _resourceView?.Refresh();
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
            case "resource":
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
            case "Resources":
                _viewModel.AddResource();
                if (_activeResourceProviders.Count == 1)
                {
                    _viewModel.SelectedResource!.Provider = _activeResourceProviders.First();
                }
                ApplyCurrentResourceContext(_viewModel.SelectedResource, GetModuleFilter("Resources"));
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
        if (filter.Equals("archived", StringComparison.OrdinalIgnoreCase))
        {
            _moduleFilters["Repository Hub"] = "all";
            filter = "all";
        }

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
        if (item is null || filter == "all")
        {
            return;
        }

        if (filter.Equals("favorites", StringComparison.OrdinalIgnoreCase))
        {
            item.IsFavorite = true;
            return;
        }

        if (filter.Equals("pinned", StringComparison.OrdinalIgnoreCase))
        {
            item.IsPinnedToRibbon = true;
            return;
        }

        item.Category = filter;
    }

    private static void ApplyCurrentCategory(ClipboardSnippetEntry? item, string filter)
    {
        if (item is null || filter == "all")
        {
            return;
        }

        if (filter.Equals("pinned", StringComparison.OrdinalIgnoreCase))
        {
            item.IsPinned = true;
            return;
        }

        item.Category = filter;
    }

    private static void ApplyCurrentResourceContext(WorkspaceResourceEntry? resource, string filter)
    {
        if (resource is null || filter == "all")
        {
            return;
        }

        if (filter.Equals("favorites", StringComparison.OrdinalIgnoreCase))
        {
            resource.IsFavorite = true;
            return;
        }

        if (filter.Equals("pinned", StringComparison.OrdinalIgnoreCase))
        {
            resource.IsPinned = true;
            return;
        }

        if (filter.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            resource.Group = filter["group:".Length..];
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

        _activeCaptureSubjects.Clear();
        _moduleFilters["Capture"] = key;
        SelectWorkspaceTab("Capture");
    }

    private void RefreshAfterDataChange()
    {
        _projectView?.Refresh();
        _portalView?.Refresh();
        _resourceView?.Refresh();
        _sidebarResourceView?.Refresh();
        _captureView?.Refresh();
        _sidebarCaptureView?.Refresh();
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
        if (_loaded)
        {
            CaptureCurrentWindowPlacement();
        }

        _viewModel.ViewMode = mode;
        ApplyViewMode(mode);
        SafeSave();
    }

    private void ApplyViewMode(WorkspaceViewMode mode)
    {
        double defaultWidth;
        double defaultHeight;

        if (mode == WorkspaceViewMode.Sidebar)
        {
            SidebarPanel.Visibility = Visibility.Visible;
            PowerOpsShell.Visibility = Visibility.Collapsed;
            MinWidth = 360;
            MinHeight = 520;
            defaultWidth = 390;
            defaultHeight = 760;
        }
        else
        {
            SidebarPanel.Visibility = Visibility.Collapsed;
            PowerOpsShell.Visibility = Visibility.Visible;

            if (mode == WorkspaceViewMode.Compact)
            {
                MinWidth = 760;
                MinHeight = 560;
                PrimaryNavColumn.Width = new GridLength(155);
                SecondaryNavColumn.Width = new GridLength(0);
                defaultWidth = 1040;
                defaultHeight = 760;
            }
            else
            {
                MinWidth = 900;
                MinHeight = 600;
                PrimaryNavColumn.Width = new GridLength(185);
                SecondaryNavColumn.Width = new GridLength(220);
                defaultWidth = 1480;
                defaultHeight = 900;
            }
        }

        WindowPlacementState placement = _viewModel.GetWindowPlacement(mode);
        Width = placement.HasSize ? Math.Max(MinWidth, placement.Width) : defaultWidth;
        Height = placement.HasSize ? Math.Max(MinHeight, placement.Height) : defaultHeight;

        bool cursorPositionOwnsSummon =
            _viewModel.WindowBehavior == WindowBehaviorMode.Summon
            && _viewModel.OpenNearCursor;

        if (placement.HasPosition && !cursorPositionOwnsSummon)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => WindowPlacementService.EnsureVisible(this)));
    }

    private void CaptureCurrentWindowPlacement()
    {
        if (!_loaded || WindowState != WindowState.Normal || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        bool includePosition =
            _viewModel.WindowBehavior != WindowBehaviorMode.Summon
            || !_viewModel.OpenNearCursor;

        _viewModel.UpdateWindowPlacement(
            _viewModel.ViewMode,
            ActualWidth,
            ActualHeight,
            Left,
            Top,
            includePosition);
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

    private void ResetWindowLayouts_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetWindowPlacements();
        ApplyViewMode(_viewModel.ViewMode);
        SafeSave(showError: true);
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
        if (_suppressAutoHide || _viewModel.WindowBehavior != WindowBehaviorMode.Summon)
        {
            return;
        }

        if (IsVisible)
        {
            CaptureCurrentWindowPlacement();
            if (!SafeSave(showError: true))
            {
                return;
            }

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

        CaptureCurrentWindowPlacement();
        if (!SafeSave(showError: true))
        {
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
            case WorkspaceResourceEntry:
                _resourceView?.Refresh();
                _sidebarResourceView?.Refresh();
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
        _portalView?.Refresh();
        RefreshQuickRibbon();
        SafeSave();
    }

    private void PortalFavoriteChanged_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _portalView?.Refresh();
        SafeSave();
    }

    private void DeletePortal_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPortal is not PortalEntry portal)
        {
            _viewModel.StatusText = "No portal selected";
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            $"Delete portal '{portal.Name}' and its {portal.Links.Count} saved sub-link(s)?",
            "Delete portal",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
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

    private void PortalList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && FindVisualAncestor<Button>(source) is not null)
        {
            return;
        }

        if (_viewModel.SelectedPortal is PortalEntry portal)
        {
            OpenUrlValue(portal.MainUrl);
            e.Handled = true;
        }
    }

    private void PortalList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_viewModel.SelectedPortal is not PortalEntry portal)
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OpenUrlValue(portal.MainUrl);
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.C
            && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            CopyText(portal.MainUrl, "Portal URL copied");
            e.Handled = true;
        }
    }

    private void SnippetList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_viewModel.SelectedSnippet is not ClipboardSnippetEntry snippet)
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.Enter
            || (e.Key == System.Windows.Input.Key.C
                && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0))
        {
            CopyText(snippet.Text, "Clipboard snippet copied");
            e.Handled = true;
        }
    }

    private void ResourceList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && FindVisualAncestor<Button>(source) is not null)
        {
            // Buttons inside a resource card own their click behavior; do not also open the row.
            return;
        }

        if (_viewModel.SelectedResource is not WorkspaceResourceEntry resource
            || string.IsNullOrWhiteSpace(resource.Url))
        {
            return;
        }

        OpenUrlValue(resource.Url);
        e.Handled = true;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ResourceList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_viewModel.SelectedResource is not WorkspaceResourceEntry resource)
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OpenUrlValue(resource.Url);
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.C
            && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            CopyText(resource.Url, "Resource URL copied");
            e.Handled = true;
        }
    }

    private void AddResource_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddResource();
        if (_activeResourceProviders.Count == 1)
        {
            _viewModel.SelectedResource!.Provider = _activeResourceProviders.First();
        }
        ApplyCurrentResourceContext(_viewModel.SelectedResource, GetModuleFilter("Resources"));
        RefreshAfterDataChange();
        SelectWorkspaceTab("Resources");
        SafeSave();
    }

    private void CaptureResourceClipboardUrl_Click(object sender, RoutedEventArgs e)
    {
        string clipboardText;
        try
        {
            if (!Clipboard.ContainsText())
            {
                _viewModel.StatusText = "Clipboard does not contain text";
                return;
            }

            clipboardText = Clipboard.GetText().Trim();
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "Clipboard unavailable";
            ShowOwnedMessage(ex.Message, "Clipboard unavailable", MessageBoxImage.Warning);
            return;
        }

        if (!ResourceCatalogService.TryClassify(clipboardText, out ResourceUrlClassification classification))
        {
            _viewModel.StatusText = "Clipboard text is not a web URL";
            return;
        }

        string currentFilter = GetModuleFilter("Resources");
        string? currentGroup = currentFilter.StartsWith("group:", StringComparison.OrdinalIgnoreCase)
            ? currentFilter["group:".Length..]
            : null;

        ResourceUpsertResult result = ResourceCatalogService.UpsertUrl(
            _viewModel.Resources,
            classification.NormalizedUrl,
            currentGroup);
        _viewModel.SelectedResource = result.Resource;

        _activeResourceProviders.Clear();
        _activeResourceProviders.Add(result.Resource.Provider);
        if (currentFilter is "favorites" or "pinned")
        {
            _moduleFilters["Resources"] = "all";
        }

        SelectWorkspaceTab("Resources");
        RefreshAfterDataChange();
        SafeSave();

        _viewModel.StatusText = result.Added
            ? $"Saved clipboard URL as {result.Resource.Provider} {result.Resource.Kind.ToLowerInvariant()}"
            : $"Resource already existed; selected {result.Resource.Name}";
    }

    private void ImportRepositoryResources_Click(object sender, RoutedEventArgs e)
    {
        RepositoryMergeSummary summary = _viewModel.ImportRepositoryResources();
        RefreshAfterDataChange();
        _viewModel.StatusText = $"Resource sync: {summary.Added} added, {summary.Updated} updated";
        SafeSave();
    }

    private void ResourceStateChanged_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded || (sender as FrameworkElement)?.DataContext is not WorkspaceResourceEntry resource)
        {
            return;
        }

        resource.UpdatedUtc = DateTimeOffset.UtcNow;
        _resourceView?.Refresh();
        _sidebarResourceView?.Refresh();
        RefreshSecondaryNavigation();
        RefreshQuickRibbon();
        SafeSave();
    }

    private void DeleteResource_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedResource is not WorkspaceResourceEntry resource)
        {
            _viewModel.StatusText = "No resource selected";
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            $"Delete resource '{resource.Name}'?\n\nOnly the local quick link is removed; the original item is untouched.",
            "Delete resource",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _viewModel.RemoveResource(resource);
        RefreshAfterDataChange();
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
        _snippetView?.Refresh();
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

        MessageBoxResult result = MessageBox.Show(
            this,
            $"Delete clipboard snippet '{snippet.Title}'?",
            "Delete snippet",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
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

    private void ArchiveProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProjectEntry project)
        {
            return;
        }

        project.IsArchived = !project.IsArchived;
        project.IncludeInCopyAll = false;
        project.UpdatedUtc = DateTimeOffset.UtcNow;
        _viewModel.StatusText = project.IsArchived ? "Project archived" : "Project restored";

        RefreshAfterDataChange();
        SafeSave();
    }

    private void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProjectEntry project)
        {
            return;
        }

        int linkedCaptures = _viewModel.Notes.Count(note => note.ProjectId == project.Id);
        int linkedResources = _viewModel.Resources.Count(resource => resource.SourceProjectId == project.Id);
        int savedListRefs = _viewModel.RepositoryLists.Sum(list => list.Items.Count(item => item.ProjectId == project.Id));
        string impact = linkedCaptures == 0 && linkedResources == 0 && savedListRefs == 0
            ? "No captures, Resource Hub links, or saved repository lists reference this project."
            : $"This will detach {linkedCaptures} capture(s) and {linkedResources} Resource Hub link(s), and remove {savedListRefs} saved-list reference(s).";

        MessageBoxResult result = MessageBox.Show(
            this,
            $"Delete project '{project.Name}'?\n\n{impact}",
            "Delete project",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _viewModel.RemoveProject(project);
        RefreshAfterDataChange();
        SafeSave();
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

    private void OpenRepoList_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RepositoryListEntry list)
        {
            return;
        }

        string[] urls = list.Items
            .Select(item => _viewModel.Projects.FirstOrDefault(project => project.Id == item.ProjectId))
            .Where(project => project is not null && !project.IsArchived && !string.IsNullOrWhiteSpace(project.RepoUrl))
            .Select(project => project!.RepoUrl.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        if (urls.Length == 0)
        {
            _viewModel.StatusText = $"Saved list '{list.Name}' has no repository URLs";
            return;
        }

        int opened = 0;
        foreach (string url in urls)
        {
            if (!UrlNormalizer.TryNormalizeOptionalWebUrl(url, out string normalized) || string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            try
            {
                Process.Start(new ProcessStartInfo(normalized) { UseShellExecute = true });
                opened++;
            }
            catch
            {
                // Continue opening the remaining saved repositories.
            }
        }

        _viewModel.StatusText = urls.Length == 20
            ? $"Opened {opened} repositories from '{list.Name}' (first 20)"
            : $"Opened {opened} repositories from '{list.Name}'";
    }

    private void DeleteRepoList_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RepositoryListEntry list)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            $"Delete saved repository list '{list.Name}'?\n\nRepository projects themselves are not deleted.",
            "Delete saved repository list",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
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

        int opened = 0;
        foreach (string url in urls)
        {
            if (!UrlNormalizer.TryNormalizeOptionalWebUrl(url, out string normalized)
                || string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            try
            {
                Process.Start(new ProcessStartInfo(normalized) { UseShellExecute = true });
                opened++;
            }
            catch
            {
                // Continue opening the remaining selected repositories.
            }
        }

        _viewModel.StatusText = urls.Length == 20
            ? $"Opened {opened} of the first 20 selected repositories"
            : $"Opened {opened} of {urls.Length} selected repositories";
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

    private void RefreshPromptVariables_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshPromptVariables();
        _viewModel.StatusText = _viewModel.PromptVariables.Count == 0
            ? "No custom prompt variables found"
            : $"Found {_viewModel.PromptVariables.Count} custom prompt variable(s)";
    }

    private void PromptModuleBody_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        _viewModel.RefreshPromptVariables();
        SafeSave();
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

    private void CaptureClipboardUrl_Click(object sender, RoutedEventArgs e)
    {
        string clipboardText;
        try
        {
            if (!Clipboard.ContainsText())
            {
                _viewModel.StatusText = "Clipboard does not contain text";
                return;
            }

            clipboardText = Clipboard.GetText().Trim();
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "Clipboard unavailable";
            ShowOwnedMessage(ex.Message, "Clipboard unavailable", MessageBoxImage.Warning);
            return;
        }

        if (!UrlNormalizer.TryNormalizeOptionalWebUrl(clipboardText, out string normalized)
            || string.IsNullOrWhiteSpace(normalized))
        {
            _viewModel.StatusText = "Clipboard text is not a web URL";
            return;
        }

        StickyNoteEntry? existingBookmark = _viewModel.Notes.FirstOrDefault(note =>
            !note.IsArchived
            && note.Kind == CaptureKind.Bookmark
            && ResourceCatalogService.UrlsEquivalent(note.Url, normalized));

        if (existingBookmark is not null)
        {
            _viewModel.SelectedNote = existingBookmark;
            _activeCaptureSubjects.Clear();
            _moduleFilters["Capture"] = CaptureKind.Bookmark.ToString();
            SelectWorkspaceTab("Capture");
            RefreshAfterDataChange();
            _viewModel.StatusText = "Bookmark already exists; selected existing capture";
            return;
        }

        PrepareCaptureAddContext();
        _viewModel.AddNote();
        if (_viewModel.SelectedNote is not StickyNoteEntry note)
        {
            return;
        }

        note.Kind = CaptureKind.Bookmark;
        note.Url = normalized;
        note.Title = ResourceCatalogService.TryClassify(normalized, out ResourceUrlClassification bookmarkInfo)
            ? bookmarkInfo.SuggestedName
            : Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
                ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)
                : "Saved URL";
        note.UpdatedUtc = DateTimeOffset.UtcNow;

        if (_activeCaptureSubjects.Count == 1)
        {
            string subject = _activeCaptureSubjects.First();
            note.Subject = subject.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : subject;
        }

        SelectWorkspaceTab("Capture");
        RefreshAfterDataChange();
        SafeSave();
        _viewModel.StatusText = "Clipboard URL captured as bookmark";
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
        _sidebarCaptureView?.Refresh();
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
            string projectName = ResolveProjectName(note.ProjectId);
            if (!string.IsNullOrWhiteSpace(projectName)) parts.Add("Project: " + projectName);
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
                    captures = captures.Select(BuildCaptureExport).ToArray(),
                };
                File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(envelope, CaptureExportJsonOptions));
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
                File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(BuildCaptureExport(note), CaptureExportJsonOptions));
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

    private IReadOnlyList<string> FormatCaptureMarkdown(StickyNoteEntry note)
    {
        List<string> lines =
        [
            "## " + (string.IsNullOrWhiteSpace(note.Title) ? "Capture" : note.Title.Trim()),
            "",
            $"- Type: {note.Kind}",
        ];
        if (!string.IsNullOrWhiteSpace(note.Subject)) lines.Add("- Subject: " + note.Subject.Trim());
        string projectName = ResolveProjectName(note.ProjectId);
        if (!string.IsNullOrWhiteSpace(projectName)) lines.Add("- Project: " + projectName);
        if (!string.IsNullOrWhiteSpace(note.Status)) lines.Add("- Status: " + note.Status.Trim());
        if (!string.IsNullOrWhiteSpace(note.Priority)) lines.Add("- Priority: " + note.Priority.Trim());
        if (note.DueUtc is not null) lines.Add("- Due: " + note.DueUtc.Value.ToString("O"));
        if (!string.IsNullOrWhiteSpace(note.Url)) lines.Add("- URL: " + note.Url.Trim());
        if (!string.IsNullOrWhiteSpace(note.Labels)) lines.Add("- Labels: " + note.Labels.Trim());
        lines.Add("");
        lines.Add(note.Text ?? string.Empty);
        return lines;
    }

    private CaptureExportEntry BuildCaptureExport(StickyNoteEntry note) => new(
        note.Id,
        note.Kind,
        note.Title,
        note.Subject,
        note.Text,
        note.Url,
        note.Labels,
        note.Status,
        note.Priority,
        note.DueUtc,
        note.ProjectId,
        ResolveProjectName(note.ProjectId),
        note.IsPinned,
        note.IsCompleted,
        note.CreatedUtc,
        note.UpdatedUtc);

    private string ResolveProjectName(Guid? projectId) =>
        projectId is Guid id
            ? _viewModel.Projects.FirstOrDefault(project => project.Id == id)?.Name ?? string.Empty
            : string.Empty;

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
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            // Read and validate the import source before touching the live workspace.
            // This matters when the user intentionally selects workspace.backup.json.
            WorkspaceState candidate = _viewModel.PrepareImport(dialog.FileName);
            string summary =
                $"Replace the current Power Ops workspace with this file?\n\n"
                + $"Projects: {candidate.Projects.Count}\n"
                + $"Saved repository lists: {candidate.RepositoryLists.Count}\n"
                + $"Portals: {candidate.Portals.Count}\n"
                + $"Resources: {candidate.Resources.Count}\n"
                + $"Captures: {candidate.Notes.Count}\n"
                + $"Clipboard snippets: {candidate.ClipboardSnippets.Count}\n"
                + $"Prompt modules: {candidate.PromptModules.Count}\n\n"
                + "Canceling leaves the current workspace unchanged.";

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                summary,
                "Confirm workspace import",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirmation != MessageBoxResult.Yes)
            {
                _viewModel.StatusText = "Import canceled";
                return;
            }

            // Preserve any in-memory edits before the import commit. The candidate is already
            // detached in memory, so this cannot overwrite the selected import source.
            _viewModel.Save();

            bool wasLoaded = _loaded;
            _loaded = false;
            try
            {
                _viewModel.CommitImport(candidate);
                _moduleFilters.Clear();
                _moduleSearchTerms.Clear();
                _activeRepositoryFamilies.Clear();
                _activeResourceProviders.Clear();
                _activeCaptureSubjects.Clear();
                _repositorySearchText = string.Empty;
                RepoSearchBox.Clear();
                _suppressShellSearchChange = true;
                try
                {
                    ShellSearchBox.Clear();
                }
                finally
                {
                    _suppressShellSearchChange = false;
                }

                InitializeWorkspaceViews();
                SelectWorkspaceTab(NormalizeModule(_viewModel.LastModule));
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

    private bool SafeSave(bool showError = false)
    {
        try
        {
            _viewModel.Save();
            return true;
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "Save failed";
            if (showError)
            {
                ShowOwnedMessage(ex.Message, "Save failed", MessageBoxImage.Error);
            }

            return false;
        }
    }
}
