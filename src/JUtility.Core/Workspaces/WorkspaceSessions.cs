using System.Text.Json;
using System.Text.Json.Serialization;
using JUtility.Core.Models;

namespace JUtility.Core.Workspaces;

public sealed record ModuleDefinition(string Id, string Title, string Header, string Category, string Purpose);

public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDefinition> All = Array.AsReadOnly(new[]
    {
        new ModuleDefinition("home", "Launchpad", "Launchpad", "Start", "Saved websites and project destinations; no background webviews."),
        new ModuleDefinition("dashboard", "Dashboard", "Dashboard", "Start", "Existing quick access and capture summary."),
        new ModuleDefinition("repositories", "Repository Hub", "Repository Hub", "Projects & tools", "Project links, categories and saved repository lists."),
        new ModuleDefinition("portals", "Portal Launcher", "Portals", "Projects & tools", "Service home pages and project-specific links."),
        new ModuleDefinition("tools", "Tool Launcher", "Tools", "Projects & tools", "Open existing local tools; do not replace an IDE."),
        new ModuleDefinition("resources", "Resource Hub", "Resources", "Projects & tools", "Exact documents, folders, dashboards and repository links."),
        new ModuleDefinition("web", "Web apps", "Web", "Projects & tools", "Web apps marked Embedded (e.g. Mongoku) inside Power Ops; WebView2 starts on first use only."),
        new ModuleDefinition("capture", "Capture", "Capture", "Knowledge & capture", "Local inbox, tasks, notes, bookmarks and transcripts."),
        new ModuleDefinition("clipboard", "Clipboard Library", "Clipboard", "Knowledge & capture", "Reusable text, screenshots and short clips."),
        new ModuleDefinition("prompts", "Prompt Builder", "Prompt Builder", "Knowledge & capture", "Reusable instruction modules and project variables."),
        new ModuleDefinition("inventory", "Environment inventory", "Environment inventory", "Device & settings", "On-demand local PATH and file-version inventory. No programs executed."),
        new ModuleDefinition("system", "System / Cheat Sheet", "System", "Device & settings", "Existing architecture facts and Windows configuration shortcuts."),
        new ModuleDefinition("features", "Feature catalog", "Feature catalog", "Device & settings", "Available features, planned adapters and lightweight boundaries."),
        new ModuleDefinition("settings", "Settings", "Settings", "Device & settings", "Existing settings and pinned Explorer folders.")
    });
    public static ModuleDefinition Get(string id) => All.FirstOrDefault(x => x.Id == id)
        ?? throw new InvalidDataException($"Unknown module: {id}");
    public static ModuleDefinition? FromHeader(string header) => All.FirstOrDefault(x => x.Header == header);
}

public sealed class SessionTab
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ModuleId { get; set; } = "home";
    public string Search { get; set; } = "";
    public string Filter { get; set; } = "all";
    public List<string> Families { get; set; } = [];
    public List<string> Providers { get; set; } = [];
    public List<string> Subjects { get; set; } = [];
}

public sealed class WorkspaceProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Workspace";
    // Visibility is not a security boundary and does not delete underlying data.
    public List<string> VisibleModules { get; set; } = ModuleCatalog.All.Select(x => x.Id).ToList();
    public List<SessionTab> Tabs { get; set; } = [new()];
    public Guid ActiveTabId { get; set; }
    public List<SessionTab> ClosedTabs { get; set; } = [];
    public WorkspaceViewMode Layout { get; set; } = WorkspaceViewMode.Expanded;
    public bool RibbonVisible { get; set; } = true;
}

public sealed class WorkspaceBookmark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Saved view";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public WorkspaceProfile View { get; set; } = new();
}

public sealed class ShellState
{
    [JsonRequired]
    public string Format { get; set; } = "powerops-shell";
    [JsonRequired]
    public int SchemaVersion { get; set; } = 1;
    public Guid ActiveWorkspaceId { get; set; }
    public List<WorkspaceProfile> Workspaces { get; set; } = [];
    public List<WorkspaceBookmark> Bookmarks { get; set; } = [];
}

public static class WorkspaceSessions
{
    public const int MaxWorkspaces = 20, MaxTabs = 16, MaxBookmarks = 20;
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter() }
    };
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    public static WorkspaceProfile Active(ShellState state) => state.Workspaces.Single(x => x.Id == state.ActiveWorkspaceId);
    public static SessionTab ActiveTab(WorkspaceProfile workspace) => workspace.Tabs.Single(x => x.Id == workspace.ActiveTabId);
    public static WorkspaceProfile NewProfile(string name, params string[] modules)
    {
        var profile = new WorkspaceProfile { Name = name };
        if (modules.Length > 0) profile.VisibleModules = modules.Concat(new[] { "home", "settings" }).Distinct().ToList();
        profile.ActiveTabId = profile.Tabs[0].Id;
        return profile;
    }
    public static ShellState Defaults()
    {
        var state = new ShellState
        {
            Workspaces =
            [
                NewProfile("General"),
                NewProfile("Datapass", "repositories", "tools", "resources", "clipboard", "prompts", "inventory"),
                NewProfile("Foil", "repositories", "portals", "resources", "capture", "prompts"),
                NewProfile("Knowledge", "resources", "capture", "clipboard", "prompts"),
                NewProfile("Device & settings", "inventory", "system", "features", "tools")
            ]
        };
        state.ActiveWorkspaceId = state.Workspaces[0].Id;
        return state;
    }
    public static SessionTab AddTab(WorkspaceProfile profile, string moduleId)
    {
        ModuleCatalog.Get(moduleId);
        if (!profile.VisibleModules.Contains(moduleId)) throw new InvalidOperationException("Enable this module in Workspace settings first.");
        if (profile.Tabs.Count >= MaxTabs) throw new InvalidOperationException($"Maximum {MaxTabs} tabs per workspace.");
        var tab = new SessionTab { ModuleId = moduleId };
        profile.Tabs.Add(tab); profile.ActiveTabId = tab.Id;
        return tab;
    }
    public static void CloseTab(WorkspaceProfile profile)
    {
        var tab = ActiveTab(profile);
        int index = profile.Tabs.IndexOf(tab);
        profile.ClosedTabs.Insert(0, Copy(tab));
        if (profile.ClosedTabs.Count > 10) profile.ClosedTabs.RemoveAt(10);
        profile.Tabs.RemoveAt(index);
        if (profile.Tabs.Count == 0) profile.Tabs.Add(new SessionTab());
        profile.ActiveTabId = profile.Tabs[Math.Min(index, profile.Tabs.Count - 1)].Id;
    }
    public static void ReopenTab(WorkspaceProfile profile)
    {
        if (profile.ClosedTabs.Count == 0) return;
        if (profile.Tabs.Count >= MaxTabs) throw new InvalidOperationException("Close a tab before reopening another.");
        var tab = profile.ClosedTabs[0]; profile.ClosedTabs.RemoveAt(0);
        if (!profile.VisibleModules.Contains(tab.ModuleId)) profile.VisibleModules.Add(tab.ModuleId);
        profile.Tabs.Add(tab); profile.ActiveTabId = tab.Id;
    }
    /// <summary>Moves the active tab by <paramref name="delta"/> with wrap-around. False when there is nothing to switch to.</summary>
    public static bool CycleTab(WorkspaceProfile profile, int delta)
    {
        if (profile.Tabs.Count < 2) return false;
        int index = profile.Tabs.FindIndex(x => x.Id == profile.ActiveTabId);
        profile.ActiveTabId = profile.Tabs[Wrap(index + delta, profile.Tabs.Count)].Id;
        return true;
    }
    /// <summary>Switches the active workspace view with wrap-around; business data is untouched.</summary>
    public static bool CycleWorkspace(ShellState state, int delta)
    {
        if (state.Workspaces.Count < 2) return false;
        int index = state.Workspaces.FindIndex(x => x.Id == state.ActiveWorkspaceId);
        state.ActiveWorkspaceId = state.Workspaces[Wrap(index + delta, state.Workspaces.Count)].Id;
        return true;
    }
    private static int Wrap(int value, int count) => ((value % count) + count) % count;
    public static void SaveView(ShellState state, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Give the saved view a name.");
        if (state.Bookmarks.Count >= MaxBookmarks) throw new InvalidOperationException("Remove a saved view before adding another (limit 20).");
        state.Bookmarks.Add(new WorkspaceBookmark { Name = name.Trim(), View = Copy(Active(state)) });
    }
    public static void RestoreView(ShellState state, Guid bookmarkId)
    {
        var view = Copy(state.Bookmarks.Single(x => x.Id == bookmarkId).View);
        int index = state.Workspaces.FindIndex(x => x.Id == view.Id);
        if (index < 0)
        {
            if (state.Workspaces.Count >= MaxWorkspaces) throw new InvalidOperationException("Workspace limit reached.");
            state.Workspaces.Add(view);
        }
        else state.Workspaces[index] = view;
        state.ActiveWorkspaceId = view.Id;
    }
    public static void SetVisibleModules(WorkspaceProfile profile, IEnumerable<string> ids)
    {
        var modules = ids.Concat(new[] { "home", "settings" }).Distinct().ToList();
        foreach (string id in modules) ModuleCatalog.Get(id);
        profile.VisibleModules = modules;
        foreach (var tab in profile.Tabs.Where(x => !modules.Contains(x.ModuleId)))
        { tab.ModuleId = "home"; tab.Search = ""; tab.Filter = "all"; tab.Families.Clear(); tab.Providers.Clear(); tab.Subjects.Clear(); }
    }
    public static void Validate(ShellState state)
    {
        if (state.Format != "powerops-shell" || state.SchemaVersion != 1) throw new InvalidDataException("Unsupported shell format/version. Existing files were not rewritten.");
        if (state.Workspaces is null || state.Workspaces.Count is < 1 or > MaxWorkspaces || state.Bookmarks is null || state.Bookmarks.Count > MaxBookmarks)
            throw new InvalidDataException("Invalid workspace/bookmark collection size.");
        Unique(state.Workspaces.Select(x => x?.Id ?? Guid.Empty));
        Unique(state.Bookmarks.Select(x => x?.Id ?? Guid.Empty));
        if (!state.Workspaces.Any(x => x.Id == state.ActiveWorkspaceId)) throw new InvalidDataException("Active workspace is missing.");
        foreach (var profile in state.Workspaces) ValidateProfile(profile);
        foreach (var bookmark in state.Bookmarks) { Text(bookmark.Name, 100); ValidateProfile(bookmark.View); }
    }
    private static void ValidateProfile(WorkspaceProfile profile)
    {
        if (profile is null || profile.Id == Guid.Empty) throw new InvalidDataException("Invalid workspace.");
        Text(profile.Name, 100);
        if (!Enum.IsDefined(profile.Layout)) throw new InvalidDataException("Invalid window layout.");
        if (profile.VisibleModules is null || profile.VisibleModules.Count > ModuleCatalog.All.Count || !profile.VisibleModules.Contains("home") || !profile.VisibleModules.Contains("settings"))
            throw new InvalidDataException("Launchpad and Settings must remain available.");
        if (profile.VisibleModules.Distinct().Count() != profile.VisibleModules.Count) throw new InvalidDataException("Duplicate module visibility entry.");
        foreach (var id in profile.VisibleModules) ModuleCatalog.Get(id);
        if (profile.Tabs is null || profile.Tabs.Count is < 1 or > MaxTabs || profile.ClosedTabs is null || profile.ClosedTabs.Count > 10)
            throw new InvalidDataException("Invalid tab collection size.");
        Unique(profile.Tabs.Concat(profile.ClosedTabs).Select(x => x?.Id ?? Guid.Empty));
        if (!profile.Tabs.Any(x => x.Id == profile.ActiveTabId)) throw new InvalidDataException("Active tab is missing.");
        foreach (var tab in profile.Tabs.Concat(profile.ClosedTabs))
        {
            ModuleCatalog.Get(tab.ModuleId); Text(tab.Search, 2048, true); Text(tab.Filter, 256, true);
            foreach (var list in new[] { tab.Families, tab.Providers, tab.Subjects })
            {
                if (list is null || list.Count > 100) throw new InvalidDataException("Invalid tab filters.");
                foreach (var value in list) Text(value, 256);
            }
        }
        if (profile.Tabs.Any(x => !profile.VisibleModules.Contains(x.ModuleId))) throw new InvalidDataException("Open tab references a hidden module.");
    }
    private static void Text(string? text, int limit, bool allowEmpty = false)
    {
        if (text is null || text.Length > limit || (!allowEmpty && string.IsNullOrWhiteSpace(text))) throw new InvalidDataException("Invalid or oversized shell text.");
    }
    private static void Unique(IEnumerable<Guid> ids)
    {
        var seen = new HashSet<Guid>();
        foreach (Guid id in ids) if (id == Guid.Empty || !seen.Add(id)) throw new InvalidDataException("Missing or duplicate identifier.");
    }
}

public sealed class ShellStateStore
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public string FilePath { get; }
    public ShellStateStore(string directory) => FilePath = Path.Combine(Path.GetFullPath(directory), "shell-workspaces.json");
    public ShellState Load()
    {
        if (!File.Exists(FilePath)) return WorkspaceSessions.Defaults();
        return Read(FilePath); // Fail closed; never silently reset malformed/future schemas.
    }
    public static ShellState Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Shell JSON exceeds 2 MiB.");
        var state = JsonSerializer.Deserialize<ShellState>(stream, WorkspaceSessions.Json) ?? throw new InvalidDataException("Empty shell JSON.");
        WorkspaceSessions.Validate(state);
        return state;
    }
    public void Save(ShellState state)
    {
        WorkspaceSessions.Validate(state);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state, WorkspaceSessions.Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Shell JSON exceeds 2 MiB.");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (File.Exists(FilePath)) _ = Read(FilePath); // Never overwrite unsupported/corrupt bytes.
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".backup", true);
            else File.Move(temporary, FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
