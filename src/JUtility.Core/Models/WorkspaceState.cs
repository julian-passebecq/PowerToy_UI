namespace JUtility.Core.Models;

public enum WorkspaceViewMode
{
    Sidebar,
    Compact,
    Expanded,
}

public enum WindowBehaviorMode
{
    Normal,
    AlwaysOnTop,
    Summon,
}

public enum SummonMouseBinding
{
    MouseButton4,
    MouseButton5,
    MiddleClick,
    CtrlMiddleClick,
}

public sealed class AppPreferences
{
    public WorkspaceViewMode LastView { get; set; } = WorkspaceViewMode.Compact;
    public WindowBehaviorMode WindowBehavior { get; set; } = WindowBehaviorMode.Normal;
    public SummonMouseBinding SummonMouseBinding { get; set; } = SummonMouseBinding.MouseButton5;
    public bool HideOnFocusLoss { get; set; } = true;
    public bool OpenNearCursor { get; set; } = true;

    // Kept for backward compatibility with schema v1 workspace files.
    public bool AlwaysOnTop { get; set; }

    public bool ShowExtraColumn { get; set; } = true;
    public string GitHubOwner { get; set; } = "julian-passebecq";
}

public sealed class ProjectEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Projects";
    public string Subcategory { get; set; } = "Misc";
    public string Note { get; set; } = string.Empty;
    public string GitHubFullName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool IsGitHubPrivate { get; set; }
    public DateTimeOffset? GitHubUpdatedUtc { get; set; }
    public string RepoUrl { get; set; } = string.Empty;
    public string SiteUrl { get; set; } = string.Empty;
    public string ServerUrl { get; set; } = string.Empty;
    public string ChatGptUrl { get; set; } = string.Empty;

    // Retained for schema-v1/v2 workspaces. New UI should prefer ServerUrl and ChatGptUrl.
    public string ExtraLabel { get; set; } = "Extra";
    public string ExtraUrl { get; set; } = string.Empty;

    public bool CopyName { get; set; } = true;
    public bool CopyRepo { get; set; } = true;
    public bool CopySite { get; set; } = true;
    public bool CopyServer { get; set; } = true;
    public bool CopyChatGpt { get; set; } = true;
    public bool CopyExtra { get; set; }
    public bool IncludeInCopyAll { get; set; } = true;
    public bool IsArchived { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RepositoryListItemEntry
{
    public Guid ProjectId { get; set; }
    public bool IncludeRepo { get; set; } = true;
    public bool IncludeSite { get; set; } = true;
    public bool IncludeServer { get; set; } = true;
    public bool IncludeChatGpt { get; set; } = true;
}

public sealed class RepositoryListEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Saved lists";
    public List<RepositoryListItemEntry> Items { get; set; } = [];
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PortalLinkEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class PortalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string IconKey { get; set; } = "↗";
    public string MainUrl { get; set; } = string.Empty;
    public bool IsPinnedToRibbon { get; set; }
    public bool IsFavorite { get; set; }
    public int SortOrder { get; set; }
    public List<PortalLinkEntry> Links { get; set; } = [];
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClipboardSnippetEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string Text { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum CaptureKind
{
    Inbox,
    Todo,
    QuickNote,
    Bookmark,
    ReadLater,
    Transcript,
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
    public CaptureKind Kind { get; set; } = CaptureKind.QuickNote;
    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Labels { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTimeOffset? DueUtc { get; set; }
    public Guid? ProjectId { get; set; }
    public bool IsPinned { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WorkspaceState
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public AppPreferences Preferences { get; set; } = new();
    public List<ProjectEntry> Projects { get; set; } = [];
    public List<RepositoryListEntry> RepositoryLists { get; set; } = [];
    public List<PortalEntry> Portals { get; set; } = [];
    public List<ClipboardSnippetEntry> ClipboardSnippets { get; set; } = [];
    public List<PromptModuleEntry> PromptModules { get; set; } = [];
    public List<RecentPromptEntry> RecentPrompts { get; set; } = [];
    public List<StickyNoteEntry> Notes { get; set; } = [];
}
