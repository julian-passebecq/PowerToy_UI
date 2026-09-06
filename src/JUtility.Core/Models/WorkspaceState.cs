namespace JUtility.Core.Models;

public enum WorkspaceViewMode
{
    Sidebar,
    Compact,
    Expanded,
}

public sealed class AppPreferences
{
    public WorkspaceViewMode LastView { get; set; } = WorkspaceViewMode.Compact;
    public bool AlwaysOnTop { get; set; }
    public bool ShowExtraColumn { get; set; } = true;
}

public sealed class ProjectEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Projects";
    public string Note { get; set; } = string.Empty;
    public string RepoUrl { get; set; } = string.Empty;
    public string SiteUrl { get; set; } = string.Empty;
    public string ExtraLabel { get; set; } = "Extra";
    public string ExtraUrl { get; set; } = string.Empty;
    public bool CopyName { get; set; } = true;
    public bool CopyRepo { get; set; } = true;
    public bool CopySite { get; set; } = true;
    public bool CopyExtra { get; set; }
    public bool IncludeInCopyAll { get; set; } = true;
    public bool IsArchived { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PromptModuleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string Body { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RecentPromptEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StickyNoteEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Labels { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WorkspaceState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public AppPreferences Preferences { get; set; } = new();
    public List<ProjectEntry> Projects { get; set; } = [];
    public List<PromptModuleEntry> PromptModules { get; set; } = [];
    public List<RecentPromptEntry> RecentPrompts { get; set; } = [];
    public List<StickyNoteEntry> Notes { get; set; } = [];
}
