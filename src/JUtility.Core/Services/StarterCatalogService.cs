using JUtility.Core.Models;

namespace JUtility.Core.Services;

public sealed record StarterCatalogSummary(int PortalsAdded, int SnippetsAdded)
{
    public int TotalAdded => PortalsAdded + SnippetsAdded;
}

public static class StarterCatalogService
{
    private sealed record PortalTemplate(
        string Name,
        string Category,
        string IconKey,
        string MainUrl,
        bool PinToRibbon = false);

    private sealed record SnippetTemplate(
        string Title,
        string Category,
        string Text,
        bool IsPinned = false);

    private static readonly PortalTemplate[] PortalTemplates =
    [
        new("GitHub", "Development", "GH", "https://github.com/", true),
        new("Microsoft Azure", "Cloud", "AZ", "https://portal.azure.com/", true),
        new("Microsoft Fabric", "Cloud", "FB", "https://app.fabric.microsoft.com/", true),
        new("Databricks", "Cloud", "DB", "https://accounts.cloud.databricks.com/", true),
        new("Vercel", "Deploy", "V", "https://vercel.com/dashboard", true),
        new("Netlify", "Deploy", "N", "https://app.netlify.com/"),
        new("Cloudflare", "Deploy", "CF", "https://dash.cloudflare.com/"),
        new("ChatGPT", "AI", "AI", "https://chatgpt.com/", true),
        new("LinkedIn", "Work", "in", "https://www.linkedin.com/"),
        new("Google Drive", "Files", "GD", "https://drive.google.com/"),
        new("Dropbox", "Files", "DBX", "https://www.dropbox.com/home"),
        new("OneDrive", "Files", "1D", "https://onedrive.live.com/"),
        new("Notion", "Work", "N", "https://www.notion.so/"),
    ];

    private static readonly SnippetTemplate[] SnippetTemplates =
    [
        new(
            "Continue project",
            "Development",
            "Continue from the current working branch and state. Preserve working behavior, debug concrete failures first, implement focused improvements, run the relevant tests, and report remaining limitations.",
            true),
        new(
            "Debug and verify",
            "Development",
            "Audit the current implementation, reproduce concrete failures where possible, fix root causes instead of symptoms, run the relevant regression tests, and summarize what changed.",
            true),
        new(
            "AI handoff",
            "Development",
            "Create a precise handoff with the repository, branch, exact commit, current architecture, implemented features, tests, known gaps, and the next concrete steps. Do not restart from an older architecture."),
    ];

    public static StarterCatalogSummary Merge(
        IList<PortalEntry> portals,
        IList<ClipboardSnippetEntry> snippets)
    {
        ArgumentNullException.ThrowIfNull(portals);
        ArgumentNullException.ThrowIfNull(snippets);

        int portalsAdded = 0;
        int snippetsAdded = 0;
        int nextPortalOrder = portals.Count == 0 ? 10 : portals.Max(portal => portal.SortOrder) + 10;
        int nextSnippetOrder = snippets.Count == 0 ? 10 : snippets.Max(snippet => snippet.SortOrder) + 10;

        foreach (PortalTemplate template in PortalTemplates)
        {
            bool exists = portals.Any(portal =>
                ResourceCatalogService.UrlsEquivalent(portal.MainUrl, template.MainUrl));

            if (exists)
            {
                continue;
            }

            portals.Add(new PortalEntry
            {
                Name = template.Name,
                Category = template.Category,
                IconKey = template.IconKey,
                MainUrl = template.MainUrl,
                IsPinnedToRibbon = template.PinToRibbon,
                SortOrder = nextPortalOrder,
                UpdatedUtc = DateTimeOffset.UtcNow,
            });
            nextPortalOrder += 10;
            portalsAdded++;
        }

        foreach (SnippetTemplate template in SnippetTemplates)
        {
            bool exists = snippets.Any(snippet =>
                string.Equals(snippet.Title.Trim(), template.Title, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                continue;
            }

            snippets.Add(new ClipboardSnippetEntry
            {
                Title = template.Title,
                Category = template.Category,
                Text = template.Text,
                IsPinned = template.IsPinned,
                SortOrder = nextSnippetOrder,
                UpdatedUtc = DateTimeOffset.UtcNow,
            });
            nextSnippetOrder += 10;
            snippetsAdded++;
        }

        return new StarterCatalogSummary(portalsAdded, snippetsAdded);
    }
}
