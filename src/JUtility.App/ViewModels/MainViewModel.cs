using System.Collections.ObjectModel;
using JUtility.Core.Models;
using JUtility.Core.Services;

namespace JUtility.App.ViewModels;

public sealed record SelectionOption<T>(T Value, string Label);

public sealed class MainViewModel : ObservableObject
{
    private readonly WorkspaceStore _store;
    private WorkspaceState _state;
    private ProjectEntry? _selectedPromptProject;
    private PortalEntry? _selectedPortal;
    private ClipboardSnippetEntry? _selectedSnippet;
    private StickyNoteEntry? _selectedNote;
    private string _promptPreview = string.Empty;
    private string _statusText = "Ready";

    public MainViewModel()
    {
        _store = new WorkspaceStore();
        _state = _store.Load();
        Projects = new ObservableCollection<ProjectEntry>(_state.Projects);
        Portals = new ObservableCollection<PortalEntry>(_state.Portals.OrderBy(item => item.SortOrder).ThenBy(item => item.Name));
        ClipboardSnippets = new ObservableCollection<ClipboardSnippetEntry>(_state.ClipboardSnippets.OrderBy(item => item.SortOrder).ThenBy(item => item.Title));
        PromptModules = new ObservableCollection<PromptModuleEntry>(_state.PromptModules.OrderBy(item => item.SortOrder));
        Notes = new ObservableCollection<StickyNoteEntry>(_state.Notes);
        RecentPrompts = new ObservableCollection<RecentPromptEntry>(_state.RecentPrompts.OrderByDescending(item => item.CreatedUtc));
        SelectedPromptProject = Projects.FirstOrDefault(project => !project.IsArchived);
        SelectedPortal = Portals.FirstOrDefault();
        SelectedSnippet = ClipboardSnippets.FirstOrDefault();
        SelectedNote = Notes.FirstOrDefault(note => !note.IsArchived);
    }

    public ObservableCollection<ProjectEntry> Projects { get; }
    public ObservableCollection<PortalEntry> Portals { get; }
    public ObservableCollection<ClipboardSnippetEntry> ClipboardSnippets { get; }
    public ObservableCollection<PromptModuleEntry> PromptModules { get; }
    public ObservableCollection<StickyNoteEntry> Notes { get; }
    public ObservableCollection<RecentPromptEntry> RecentPrompts { get; }

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

    public IReadOnlyList<CaptureKind> CaptureKindOptions { get; } = Enum.GetValues<CaptureKind>();

    public PortalEntry? SelectedPortal
    {
        get => _selectedPortal;
        set => SetProperty(ref _selectedPortal, value);
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
        set => SetProperty(ref _selectedNote, value);
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

    public string DataFilePath => _store.DataFilePath;
    public string DataDirectory => _store.DataDirectory;

    public void AddProject()
    {
        ProjectEntry project = new()
        {
            Name = "New project",
            Category = "Projects",
            UpdatedUtc = DateTimeOffset.UtcNow,
        };
        Projects.Add(project);
        StatusText = "Project added";
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

    public void RemoveProject(ProjectEntry project)
    {
        Projects.Remove(project);
        if (ReferenceEquals(SelectedPromptProject, project))
        {
            SelectedPromptProject = Projects.FirstOrDefault(item => !item.IsArchived);
        }
        StatusText = "Project removed";
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
        PromptPreview = PromptComposer.Compose(PromptModules, SelectedPromptProject, appendProjectLinks: appendProjectLinks);
        return PromptPreview;
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

    public void Import(string path)
    {
        _state = _store.Import(path);
        ReplaceCollection(Projects, _state.Projects);
        ReplaceCollection(Portals, _state.Portals.OrderBy(item => item.SortOrder).ThenBy(item => item.Name));
        ReplaceCollection(ClipboardSnippets, _state.ClipboardSnippets.OrderBy(item => item.SortOrder).ThenBy(item => item.Title));
        ReplaceCollection(PromptModules, _state.PromptModules.OrderBy(item => item.SortOrder));
        ReplaceCollection(Notes, _state.Notes);
        ReplaceCollection(RecentPrompts, _state.RecentPrompts.OrderByDescending(item => item.CreatedUtc));
        SelectedPromptProject = Projects.FirstOrDefault(project => !project.IsArchived);
        SelectedPortal = Portals.FirstOrDefault();
        SelectedSnippet = ClipboardSnippets.FirstOrDefault();
        SelectedNote = Notes.FirstOrDefault(note => !note.IsArchived);
        RaisePropertyChanged(nameof(ViewMode));
        RaisePropertyChanged(nameof(WindowBehavior));
        RaisePropertyChanged(nameof(SummonMouseBinding));
        RaisePropertyChanged(nameof(HideOnFocusLoss));
        RaisePropertyChanged(nameof(OpenNearCursor));
        RaisePropertyChanged(nameof(ShowExtraColumn));
        StatusText = "Workspace imported";
    }

    private void SyncState()
    {
        _state.Projects = Projects.ToList();
        _state.Portals = Portals.ToList();
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
