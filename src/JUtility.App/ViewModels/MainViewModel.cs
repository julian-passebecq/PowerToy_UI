using System.Collections.ObjectModel;
using JUtility.App.Services;
using JUtility.Core.Models;
using JUtility.Core.Services;

namespace JUtility.App.ViewModels;

public sealed record SelectionOption<T>(T Value, string Label);
public sealed record RepositoryMergeSummary(int Added, int Updated, int Total);

public sealed class PromptVariableInput : ObservableObject
{
    private string _value = string.Empty;

    public PromptVariableInput(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly WorkspaceStore _store;
    private WorkspaceState _state;
    private ProjectEntry? _selectedPromptProject;
    private PortalEntry? _selectedPortal;
    private WorkspaceResourceEntry? _selectedResource;
    private ClipboardSnippetEntry? _selectedSnippet;
    private StickyNoteEntry? _selectedNote;
    private string _promptPreview = string.Empty;
    private string _statusText = "Ready";

    public MainViewModel()
        : this(new WorkspaceStore())
    {
    }

    public MainViewModel(WorkspaceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _state = _store.Load();
        Projects = new ObservableCollection<ProjectEntry>(_state.Projects);
        RepositoryLists = new ObservableCollection<RepositoryListEntry>(_state.RepositoryLists.OrderByDescending(item => item.UpdatedUtc));
        Portals = new ObservableCollection<PortalEntry>(_state.Portals.OrderBy(item => item.SortOrder).ThenBy(item => item.Name));
        Resources = new ObservableCollection<WorkspaceResourceEntry>(_state.Resources.OrderBy(item => item.SortOrder).ThenBy(item => item.Name));
        ClipboardSnippets = new ObservableCollection<ClipboardSnippetEntry>(_state.ClipboardSnippets.OrderBy(item => item.SortOrder).ThenBy(item => item.Title));
        PromptModules = new ObservableCollection<PromptModuleEntry>(_state.PromptModules.OrderBy(item => item.SortOrder));
        Notes = new ObservableCollection<StickyNoteEntry>(_state.Notes);
        RecentPrompts = new ObservableCollection<RecentPromptEntry>(_state.RecentPrompts.OrderByDescending(item => item.CreatedUtc));
        PromptVariables = new ObservableCollection<PromptVariableInput>();
        SelectedPromptProject = Projects.FirstOrDefault(project => !project.IsArchived);
        SelectedPortal = Portals.FirstOrDefault();
        SelectedResource = Resources.FirstOrDefault();
        SelectedSnippet = ClipboardSnippets.FirstOrDefault();
        SelectedNote = Notes.FirstOrDefault(note => !note.IsArchived);
    }

    public ObservableCollection<ProjectEntry> Projects { get; }
    public ObservableCollection<RepositoryListEntry> RepositoryLists { get; }
    public ObservableCollection<PortalEntry> Portals { get; }
    public ObservableCollection<WorkspaceResourceEntry> Resources { get; }
    public ObservableCollection<ClipboardSnippetEntry> ClipboardSnippets { get; }
    public ObservableCollection<PromptModuleEntry> PromptModules { get; }
    public ObservableCollection<StickyNoteEntry> Notes { get; }
    public ObservableCollection<RecentPromptEntry> RecentPrompts { get; }
    public ObservableCollection<PromptVariableInput> PromptVariables { get; }

    public IReadOnlyList<SelectionOption<WindowBehaviorMode>> WindowBehaviorOptions { get; } =
    [
        new(WindowBehaviorMode.Normal, "Normal"),
        new(WindowBehaviorMode.AlwaysOnTop, "Always on top"),
        new(WindowBehaviorMode.Summon, "Summon / hide"),
    ];

    public IReadOnlyList<SelectionOption<SummonMouseBinding>> SummonBindingOptions { get; } =
    [
        new(SummonMouseBinding.MouseButton4, "Mouse button 4"),
        new(SummonMouseBinding.MouseButton5, "Mouse button 5"),
        new(SummonMouseBinding.MiddleClick, "Middle click"),
        new(SummonMouseBinding.CtrlMiddleClick, "Ctrl + middle click"),
    ];

    public IReadOnlyList<string> ResourceProviderOptions { get; } =
    [
        "GitHub",
        "Google Drive",
        "Dropbox",
        "OneDrive",
        "SharePoint",
        "Notion",
        "Other",
    ];

    public IReadOnlyList<string> ResourceKindOptions { get; } =
    [
        "Repository",
        "Folder",
        "Document",
        "Dashboard",
        "Page",
        "Link",
    ];

    public IReadOnlyList<CaptureKind> CaptureKindOptions { get; } = Enum.GetValues<CaptureKind>();

    public PortalEntry? SelectedPortal
    {
        get => _selectedPortal;
        set => SetProperty(ref _selectedPortal, value);
    }

    public WorkspaceResourceEntry? SelectedResource
    {
        get => _selectedResource;
        set => SetProperty(ref _selectedResource, value);
    }

    public ClipboardSnippetEntry? SelectedSnippet
    {
        get => _selectedSnippet;
        set => SetProperty(ref _selectedSnippet, value);
    }

    public ProjectEntry? SelectedPromptProject
    {
        get => _selectedPromptProject;
        set => SetProperty(ref _selectedPromptProject, value);
    }

    public StickyNoteEntry? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (SetProperty(ref _selectedNote, value))
            {
                RaisePropertyChanged(nameof(SelectedNoteDueDate));
            }
        }
    }

    public DateTime? SelectedNoteDueDate
    {
        get => SelectedNote?.DueUtc?.LocalDateTime.Date;
        set
        {
            if (SelectedNote is null)
            {
                return;
            }

            DateTimeOffset? normalized = value is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Local));

            if (SelectedNote.DueUtc == normalized)
            {
                return;
            }

            SelectedNote.DueUtc = normalized;
            SelectedNote.UpdatedUtc = DateTimeOffset.UtcNow;
            RaisePropertyChanged();
        }
    }

    public string PromptPreview
    {
        get => _promptPreview;
        set => SetProperty(ref _promptPreview, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public WorkspaceViewMode ViewMode
    {
        get => _state.Preferences.LastView;
        set
        {
            if (_state.Preferences.LastView == value)
            {
                return;
            }

            _state.Preferences.LastView = value;
            RaisePropertyChanged();
        }
    }

    public void ResetWindowPlacements()
    {
        _state.Preferences.SidebarPlacement = new WindowPlacementState();
        _state.Preferences.CompactPlacement = new WindowPlacementState();
        _state.Preferences.ExpandedPlacement = new WindowPlacementState();
        StatusText = "Saved window layouts reset";
    }

    public WindowPlacementState GetWindowPlacement(WorkspaceViewMode mode) => mode switch
    {
        WorkspaceViewMode.Sidebar => _state.Preferences.SidebarPlacement,
        WorkspaceViewMode.Compact => _state.Preferences.CompactPlacement,
        WorkspaceViewMode.Expanded => _state.Preferences.ExpandedPlacement,
        _ => _state.Preferences.CompactPlacement,
    };

    public void UpdateWindowPlacement(
        WorkspaceViewMode mode,
        double width,
        double height,
        double left,
        double top,
        bool includePosition)
    {
        WindowPlacementState placement = GetWindowPlacement(mode);
        if (double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0)
        {
            placement.Width = width;
            placement.Height = height;
            placement.HasSize = true;
        }

        if (includePosition && double.IsFinite(left) && double.IsFinite(top))
        {
            placement.Left = left;
            placement.Top = top;
            placement.HasPosition = true;
        }
    }

    public WindowBehaviorMode WindowBehavior
    {
        get => _state.Preferences.WindowBehavior;
        set
        {
            if (_state.Preferences.WindowBehavior == value)
            {
                return;
            }

            _state.Preferences.WindowBehavior = value;
            _state.Preferences.AlwaysOnTop = value == WindowBehaviorMode.AlwaysOnTop;
            RaisePropertyChanged();
        }
    }

    public SummonMouseBinding SummonMouseBinding
    {
        get => _state.Preferences.SummonMouseBinding;
        set
        {
            if (_state.Preferences.SummonMouseBinding == value)
            {
                return;
            }

            _state.Preferences.SummonMouseBinding = value;
            RaisePropertyChanged();
        }
    }

    public bool HideOnFocusLoss
    {
        get => _state.Preferences.HideOnFocusLoss;
        set
        {
            if (_state.Preferences.HideOnFocusLoss == value)
            {
                return;
            }

            _state.Preferences.HideOnFocusLoss = value;
            RaisePropertyChanged();
        }
    }

    public bool OpenNearCursor
    {
        get => _state.Preferences.OpenNearCursor;
        set
        {
            if (_state.Preferences.OpenNearCursor == value)
            {
                return;
            }

            _state.Preferences.OpenNearCursor = value;
            RaisePropertyChanged();
        }
    }

    public bool ShowExtraColumn
    {
        get => _state.Preferences.ShowExtraColumn;
        set
        {
            if (_state.Preferences.ShowExtraColumn == value)
            {
                return;
            }

            _state.Preferences.ShowExtraColumn = value;
            RaisePropertyChanged();
        }
    }

    public bool IncludeCompletedCaptures
    {
        get => _state.Preferences.IncludeCompletedCaptures;
        set
        {
            if (_state.Preferences.IncludeCompletedCaptures == value)
            {
                return;
            }

            _state.Preferences.IncludeCompletedCaptures = value;
            RaisePropertyChanged();
        }
    }

    public string GitHubOwner
    {
        get => _state.Preferences.GitHubOwner;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "julian-passebecq" : value.Trim();
            if (string.Equals(_state.Preferences.GitHubOwner, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _state.Preferences.GitHubOwner = normalized;
            RaisePropertyChanged();
        }
    }

    public string LastModule
    {
        get => _state.Preferences.LastModule;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "Dashboard" : value.Trim();
            if (string.Equals(_state.Preferences.LastModule, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _state.Preferences.LastModule = normalized;
            RaisePropertyChanged();
        }
    }

    public string DataFilePath => _store.DataFilePath;
    public string DataDirectory => _store.DataDirectory;

    public RepositoryMergeSummary MergeGitHubRepositories(IEnumerable<GitHubRepositorySnapshot> repositories)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        int added = 0;
        int updated = 0;

        foreach (GitHubRepositorySnapshot snapshot in repositories)
        {
            string normalizedUrl = snapshot.Url.TrimEnd('/');
            ProjectEntry? existing = Projects.FirstOrDefault(project =>
                (!string.IsNullOrWhiteSpace(project.GitHubFullName)
                    && string.Equals(project.GitHubFullName, snapshot.FullName, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(project.RepoUrl)
                    && string.Equals(project.RepoUrl.TrimEnd('/'), normalizedUrl, StringComparison.OrdinalIgnoreCase)));

            if (existing is null)
            {
                existing = new ProjectEntry
                {
                    Name = snapshot.Name,
                    Category = DetectProjectFamily(snapshot.Name),
                    Subcategory = DetectProjectSubcategory(DetectProjectFamily(snapshot.Name), snapshot.Name),
                    Note = snapshot.Description,
                    GitHubFullName = snapshot.FullName,
                    Language = snapshot.Language,
                    IsGitHubPrivate = snapshot.IsPrivate,
                    GitHubUpdatedUtc = snapshot.UpdatedUtc,
                    RepoUrl = snapshot.Url,
                    SiteUrl = snapshot.Homepage,
                    CopyName = false,
                    CopyRepo = true,
                    CopySite = !string.IsNullOrWhiteSpace(snapshot.Homepage),
                    CopyServer = false,
                    CopyChatGpt = false,
                    IncludeInCopyAll = false,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                };
                Projects.Add(existing);
                added++;
                continue;
            }

            existing.GitHubFullName = snapshot.FullName;
            existing.Language = snapshot.Language;
            existing.IsGitHubPrivate = snapshot.IsPrivate;
            existing.GitHubUpdatedUtc = snapshot.UpdatedUtc;
            existing.RepoUrl = snapshot.Url;
            if (string.IsNullOrWhiteSpace(existing.SiteUrl) && !string.IsNullOrWhiteSpace(snapshot.Homepage))
            {
                existing.SiteUrl = snapshot.Homepage;
            }
            if (string.IsNullOrWhiteSpace(existing.Note) && !string.IsNullOrWhiteSpace(snapshot.Description))
            {
                existing.Note = snapshot.Description;
            }
            if (string.IsNullOrWhiteSpace(existing.Category)
                || string.Equals(existing.Category, "Projects", StringComparison.OrdinalIgnoreCase)
                || string.Equals(existing.Category, "Other", StringComparison.OrdinalIgnoreCase))
            {
                existing.Category = DetectProjectFamily(snapshot.Name);
            }
            if (string.IsNullOrWhiteSpace(existing.Subcategory)
                || string.Equals(existing.Subcategory, "Misc", StringComparison.OrdinalIgnoreCase))
            {
                existing.Subcategory = DetectProjectSubcategory(existing.Category, snapshot.Name);
            }
            existing.UpdatedUtc = DateTimeOffset.UtcNow;
            updated++;
        }

        StatusText = $"GitHub sync: {added} added, {updated} updated";
        return new RepositoryMergeSummary(added, updated, Projects.Count);
    }

    private static string DetectProjectFamily(string name)
    {
        if (name.StartsWith("foil", StringComparison.OrdinalIgnoreCase)
            || name.Contains("databricks-vscode-foil", StringComparison.OrdinalIgnoreCase)) return "Foil";
        if (name.StartsWith("atlas", StringComparison.OrdinalIgnoreCase)) return "Atlas";
        if (name.Contains("datapass", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ducklab", StringComparison.OrdinalIgnoreCase)) return "Datapass";
        if (name.Contains("fabric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
        if (name.Contains("oracle", StringComparison.OrdinalIgnoreCase)
            || name.Contains("infra", StringComparison.OrdinalIgnoreCase)
            || name.Contains("grafana", StringComparison.OrdinalIgnoreCase)) return "Infra";
        if (name.Contains("portfolio", StringComparison.OrdinalIgnoreCase)
            || name.Contains("julianvue", StringComparison.OrdinalIgnoreCase)
            || name.Contains("bisite", StringComparison.OrdinalIgnoreCase)) return "Portfolio";
        return "Other";
    }

    private static string DetectProjectSubcategory(string family, string name)
    {
        if (family.Equals("Foil", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("extension", StringComparison.OrdinalIgnoreCase)
                || name.Contains("vscode", StringComparison.OrdinalIgnoreCase)
                || name.Contains("databrick", StringComparison.OrdinalIgnoreCase)) return "Extensions";
            if (name.Contains("3d", StringComparison.OrdinalIgnoreCase)
                || name.Contains("web", StringComparison.OrdinalIgnoreCase)
                || name.Contains("pptx", StringComparison.OrdinalIgnoreCase)) return "Experiments";
            return "Core";
        }

        if (family.Equals("Atlas", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("mongo", StringComparison.OrdinalIgnoreCase)
                || name.Contains("data", StringComparison.OrdinalIgnoreCase)) return "Data";
            if (name.Contains("note", StringComparison.OrdinalIgnoreCase)
                || name.Contains("code", StringComparison.OrdinalIgnoreCase)) return "Core";
            return "Tools";
        }

        if (family.Equals("Datapass", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("airflow", StringComparison.OrdinalIgnoreCase)
                || name.Contains("pipeline", StringComparison.OrdinalIgnoreCase)
                || name.Contains("runner", StringComparison.OrdinalIgnoreCase)) return "Pipelines";
            if (name.Contains("connector", StringComparison.OrdinalIgnoreCase)
                || name.Contains("api", StringComparison.OrdinalIgnoreCase)) return "Connectors";
            if (name.Contains("ducklab", StringComparison.OrdinalIgnoreCase)
                || name.Contains("studio", StringComparison.OrdinalIgnoreCase)
                || name.Contains("workbench", StringComparison.OrdinalIgnoreCase)) return "Workbench";
            return "Core";
        }

        if (family.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
        {
            return name.Contains("sample", StringComparison.OrdinalIgnoreCase)
                || name.Contains("contoso", StringComparison.OrdinalIgnoreCase)
                ? "Samples"
                : "Tools";
        }

        if (family.Equals("Infra", StringComparison.OrdinalIgnoreCase))
        {
            return name.Contains("grafana", StringComparison.OrdinalIgnoreCase)
                || name.Contains("monitor", StringComparison.OrdinalIgnoreCase)
                ? "Monitoring"
                : "DevOps";
        }

        if (family.Equals("Portfolio", StringComparison.OrdinalIgnoreCase))
        {
            return name.Contains("showcase", StringComparison.OrdinalIgnoreCase) ? "Showcase" : "Apps";
        }

        return "Other";
    }

    public void AddProject()
    {
        ProjectEntry project = new()
        {
            Name = "New project",
            Category = "Projects",
            Subcategory = "Misc",
            UpdatedUtc = DateTimeOffset.UtcNow,
        };
        Projects.Add(project);
        StatusText = "Project added";
    }

    public void AddResource()
    {
        WorkspaceResourceEntry resource = new()
        {
            Name = "New resource",
            Provider = "Other",
            Kind = "Link",
            Group = "General",
            SortOrder = Resources.Count == 0 ? 10 : Resources.Max(item => item.SortOrder) + 10,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };
        Resources.Add(resource);
        SelectedResource = resource;
        StatusText = "Resource added";
    }

    public void RemoveResource(WorkspaceResourceEntry resource)
    {
        Resources.Remove(resource);
        if (ReferenceEquals(SelectedResource, resource))
        {
            SelectedResource = Resources.FirstOrDefault();
        }
        StatusText = "Resource removed";
    }

    public RepositoryMergeSummary ImportRepositoryResources()
    {
        ResourceImportSummary summary = ResourceCatalogService.ImportProjects(Resources, Projects);
        StatusText = $"Resource sync: {summary.Added} added, {summary.Updated} updated";
        return new RepositoryMergeSummary(summary.Added, summary.Updated, summary.Total);
    }

    public void AddPortal()
    {
        PortalEntry portal = new()
        {
            Name = "New portal",
            Category = "General",
            SortOrder = Portals.Count == 0 ? 10 : Portals.Max(item => item.SortOrder) + 10,
        };
        Portals.Add(portal);
        SelectedPortal = portal;
        StatusText = "Portal added";
    }

    public void RemovePortal(PortalEntry portal)
    {
        Portals.Remove(portal);
        if (ReferenceEquals(SelectedPortal, portal))
        {
            SelectedPortal = Portals.FirstOrDefault();
        }
        StatusText = "Portal removed";
    }

    public void AddPortalLink(PortalEntry portal)
    {
        portal.Links ??= [];
        portal.Links.Add(new PortalLinkEntry
        {
            Label = "New link",
            SortOrder = portal.Links.Count == 0 ? 10 : portal.Links.Max(item => item.SortOrder) + 10,
        });
        portal.UpdatedUtc = DateTimeOffset.UtcNow;
        StatusText = "Portal link added";
        RaisePropertyChanged(nameof(SelectedPortal));
    }

    public void AddClipboardSnippet()
    {
        ClipboardSnippetEntry snippet = new()
        {
            Title = "New snippet",
            Category = "General",
            Text = "Paste reusable text here.",
            SortOrder = ClipboardSnippets.Count == 0 ? 10 : ClipboardSnippets.Max(item => item.SortOrder) + 10,
        };
        ClipboardSnippets.Add(snippet);
        SelectedSnippet = snippet;
        StatusText = "Clipboard snippet added";
    }

    public void RemoveClipboardSnippet(ClipboardSnippetEntry snippet)
    {
        ClipboardSnippets.Remove(snippet);
        if (ReferenceEquals(SelectedSnippet, snippet))
        {
            SelectedSnippet = ClipboardSnippets.FirstOrDefault();
        }
        StatusText = "Clipboard snippet removed";
    }

    public RepositoryListEntry SaveRepositoryList(string name)
    {
        string normalizedName = string.IsNullOrWhiteSpace(name) ? "Saved list" : name.Trim();
        RepositoryListEntry list = new()
        {
            Name = normalizedName,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Items = Projects
                .Where(project => project.IncludeInCopyAll && !project.IsArchived)
                .Select(project => new RepositoryListItemEntry
                {
                    ProjectId = project.Id,
                    IncludeRepo = project.CopyRepo,
                    IncludeSite = project.CopySite,
                    IncludeServer = project.CopyServer,
                    IncludeChatGpt = project.CopyChatGpt,
                })
                .ToList(),
        };

        RepositoryLists.Insert(0, list);
        StatusText = $"Saved repository list '{normalizedName}'";
        return list;
    }

    public void ApplyRepositoryList(RepositoryListEntry list)
    {
        ArgumentNullException.ThrowIfNull(list);
        Dictionary<Guid, RepositoryListItemEntry> items = list.Items
            .GroupBy(item => item.ProjectId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (ProjectEntry project in Projects)
        {
            bool included = items.TryGetValue(project.Id, out RepositoryListItemEntry? item);
            project.IncludeInCopyAll = included;
            if (!included || item is null)
            {
                continue;
            }

            project.CopyRepo = item.IncludeRepo && !string.IsNullOrWhiteSpace(project.RepoUrl);
            project.CopySite = item.IncludeSite && !string.IsNullOrWhiteSpace(project.SiteUrl);
            project.CopyServer = item.IncludeServer && !string.IsNullOrWhiteSpace(project.ServerUrl);
            project.CopyChatGpt = item.IncludeChatGpt && !string.IsNullOrWhiteSpace(project.ChatGptUrl);
        }

        list.UpdatedUtc = DateTimeOffset.UtcNow;
        StatusText = $"Loaded repository list '{list.Name}'";
    }

    public string FormatRepositoryList(RepositoryListEntry list)
    {
        ArgumentNullException.ThrowIfNull(list);
        Dictionary<Guid, RepositoryListItemEntry> items = list.Items
            .GroupBy(item => item.ProjectId)
            .ToDictionary(group => group.Key, group => group.First());

        List<string> lines = [];
        foreach (ProjectEntry project in Projects)
        {
            if (!items.TryGetValue(project.Id, out RepositoryListItemEntry? item) || item is null || project.IsArchived)
            {
                continue;
            }

            List<string> urls = [];
            if (item.IncludeRepo && !string.IsNullOrWhiteSpace(project.RepoUrl)) urls.Add(project.RepoUrl.Trim());
            if (item.IncludeSite && !string.IsNullOrWhiteSpace(project.SiteUrl)) urls.Add(project.SiteUrl.Trim());
            if (item.IncludeServer && !string.IsNullOrWhiteSpace(project.ServerUrl)) urls.Add(project.ServerUrl.Trim());
            if (item.IncludeChatGpt && !string.IsNullOrWhiteSpace(project.ChatGptUrl)) urls.Add(project.ChatGptUrl.Trim());
            if (urls.Count > 0)
            {
                lines.Add(string.Join(" ", urls));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public void RemoveRepositoryList(RepositoryListEntry list)
    {
        RepositoryLists.Remove(list);
        StatusText = "Saved repository list removed";
    }

    public void RemoveProject(ProjectEntry project)
    {
        ArgumentNullException.ThrowIfNull(project);

        foreach (StickyNoteEntry note in Notes.Where(note => note.ProjectId == project.Id))
        {
            note.ProjectId = null;
            note.UpdatedUtc = DateTimeOffset.UtcNow;
        }

        foreach (RepositoryListEntry list in RepositoryLists)
        {
            int removed = list.Items.RemoveAll(item => item.ProjectId == project.Id);
            if (removed > 0)
            {
                list.UpdatedUtc = DateTimeOffset.UtcNow;
            }
        }

        foreach (WorkspaceResourceEntry resource in Resources.Where(resource => resource.SourceProjectId == project.Id))
        {
            resource.SourceProjectId = null;
            resource.UpdatedUtc = DateTimeOffset.UtcNow;
        }

        Projects.Remove(project);
        if (ReferenceEquals(SelectedPromptProject, project))
        {
            SelectedPromptProject = Projects.FirstOrDefault(item => !item.IsArchived);
        }

        StatusText = "Project removed; linked captures/resources were detached and saved lists updated";
    }

    public void AddPromptModule()
    {
        int nextOrder = PromptModules.Count == 0 ? 10 : PromptModules.Max(item => item.SortOrder) + 10;
        PromptModules.Add(new PromptModuleEntry
        {
            Title = "New module",
            Category = "General",
            Body = "Edit this reusable prompt block.",
            SortOrder = nextOrder,
        });
        StatusText = "Prompt module added";
    }

    public void MoveModule(PromptModuleEntry module, int delta)
    {
        int index = PromptModules.IndexOf(module);
        int target = Math.Clamp(index + delta, 0, PromptModules.Count - 1);
        if (index < 0 || index == target)
        {
            return;
        }

        PromptModules.Move(index, target);
        RenumberModules();
        StatusText = "Prompt modules reordered";
    }

    public string ComposePrompt(bool appendProjectLinks)
    {
        RenumberModules();
        RefreshPromptVariables();

        Dictionary<string, string> variables = PromptVariables
            .Where(input => !string.IsNullOrWhiteSpace(input.Value))
            .ToDictionary(input => input.Name, input => input.Value, StringComparer.OrdinalIgnoreCase);

        PromptPreview = PromptComposer.Compose(
            PromptModules,
            SelectedPromptProject,
            variables,
            appendProjectLinks: appendProjectLinks);
        return PromptPreview;
    }

    public void RefreshPromptVariables()
    {
        HashSet<string> builtIns = new(StringComparer.OrdinalIgnoreCase)
        {
            "project",
            "repo",
            "site",
            "server",
            "chatgpt",
            "extra",
        };

        string[] required = PromptComposer.FindVariables(PromptModules)
            .Where(variable => !builtIns.Contains(variable))
            .ToArray();

        Dictionary<string, string> existing = PromptVariables
            .ToDictionary(input => input.Name, input => input.Value, StringComparer.OrdinalIgnoreCase);

        PromptVariables.Clear();
        foreach (string variable in required)
        {
            PromptVariableInput input = new(variable);
            if (existing.TryGetValue(variable, out string? value))
            {
                input.Value = value;
            }
            PromptVariables.Add(input);
        }
    }

    public void RecordRecentPrompt(string text)
    {
        SyncState();
        WorkspaceStore.AddRecentPrompt(_state, "Composed prompt", text);
        RefreshRecentPrompts();
    }

    public void AddNote()
    {
        StickyNoteEntry note = new()
        {
            Title = "New note",
            Text = string.Empty,
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };
        Notes.Add(note);
        SelectedNote = note;
        StatusText = "Note added";
    }

    public void RemoveNote(StickyNoteEntry note)
    {
        Notes.Remove(note);
        if (ReferenceEquals(SelectedNote, note))
        {
            SelectedNote = Notes.FirstOrDefault(item => !item.IsArchived) ?? Notes.FirstOrDefault();
        }
        StatusText = "Capture deleted";
    }

    public void ArchiveOrRestoreNote(StickyNoteEntry note)
    {
        note.IsArchived = !note.IsArchived;
        note.UpdatedUtc = DateTimeOffset.UtcNow;
        StatusText = note.IsArchived ? "Note archived" : "Note restored";
    }

    public void Save()
    {
        SyncState();
        _store.Save(_state);
        StatusText = "Saved";
    }

    public void Export(string path)
    {
        SyncState();
        _store.Export(_state, path);
        StatusText = "Workspace exported";
    }

    public WorkspaceState PrepareImport(string path) =>
        _store.PrepareImport(path);

    public void CommitImport(WorkspaceState candidate)
    {
        _state = _store.CommitImport(candidate);
        ApplyImportedState();
        StatusText = "Workspace imported";
    }

    public void Import(string path) =>
        CommitImport(PrepareImport(path));

    private void ApplyImportedState()
    {
        ReplaceCollection(Projects, _state.Projects);
        ReplaceCollection(RepositoryLists, _state.RepositoryLists.OrderByDescending(item => item.UpdatedUtc));
        ReplaceCollection(Portals, _state.Portals.OrderBy(item => item.SortOrder).ThenBy(item => item.Name));
        ReplaceCollection(Resources, _state.Resources.OrderBy(item => item.SortOrder).ThenBy(item => item.Name));
        ReplaceCollection(ClipboardSnippets, _state.ClipboardSnippets.OrderBy(item => item.SortOrder).ThenBy(item => item.Title));
        ReplaceCollection(PromptModules, _state.PromptModules.OrderBy(item => item.SortOrder));
        ReplaceCollection(Notes, _state.Notes);
        ReplaceCollection(RecentPrompts, _state.RecentPrompts.OrderByDescending(item => item.CreatedUtc));

        SelectedPromptProject = Projects.FirstOrDefault(project => !project.IsArchived);
        SelectedPortal = Portals.FirstOrDefault();
        SelectedResource = Resources.FirstOrDefault();
        SelectedSnippet = ClipboardSnippets.FirstOrDefault();
        SelectedNote = Notes.FirstOrDefault(note => !note.IsArchived);
        PromptVariables.Clear();
        PromptPreview = string.Empty;

        RaisePropertyChanged(nameof(ViewMode));
        RaisePropertyChanged(nameof(WindowBehavior));
        RaisePropertyChanged(nameof(SummonMouseBinding));
        RaisePropertyChanged(nameof(HideOnFocusLoss));
        RaisePropertyChanged(nameof(OpenNearCursor));
        RaisePropertyChanged(nameof(ShowExtraColumn));
        RaisePropertyChanged(nameof(IncludeCompletedCaptures));
        RaisePropertyChanged(nameof(GitHubOwner));
        RaisePropertyChanged(nameof(LastModule));
    }

    private void SyncState()
    {
        _state.Projects = Projects.ToList();
        _state.RepositoryLists = RepositoryLists.ToList();
        _state.Portals = Portals.ToList();
        _state.Resources = Resources.ToList();
        _state.ClipboardSnippets = ClipboardSnippets.ToList();
        _state.PromptModules = PromptModules.ToList();
        _state.Notes = Notes.ToList();
        _state.RecentPrompts = RecentPrompts.ToList();
    }

    private void RenumberModules()
    {
        for (int index = 0; index < PromptModules.Count; index++)
        {
            PromptModules[index].SortOrder = (index + 1) * 10;
        }
    }

    private void RefreshRecentPrompts()
    {
        ReplaceCollection(RecentPrompts, _state.RecentPrompts.OrderByDescending(item => item.CreatedUtc));
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source)
        {
            target.Add(item);
        }
    }
}
