using JUtility.Core.Models;

namespace JUtility.Core.Services;

public static class SidebarQuickAccessPolicy
{
    public static bool IncludePortal(PortalEntry portal)
    {
        ArgumentNullException.ThrowIfNull(portal);
        return portal.IsPinnedToRibbon || portal.IsFavorite;
    }

    public static bool IncludeResource(WorkspaceResourceEntry resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return resource.IsPinned || resource.IsFavorite;
    }

    public static bool IncludeSnippet(ClipboardSnippetEntry snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        return snippet.IsPinned;
    }

    public static bool IncludeCapture(StickyNoteEntry note, bool includeCompleted)
    {
        ArgumentNullException.ThrowIfNull(note);
        return !note.IsArchived && (includeCompleted || !note.IsCompleted);
    }
}
