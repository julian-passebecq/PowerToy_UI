using JUtility.Core.Models;

namespace JUtility.Core.Services;

public sealed record StarterCatalogSummary(int PortalsAdded, int SnippetsAdded)
{
    public int TotalAdded => PortalsAdded + SnippetsAdded;
}

public static class StarterCatalogService
{
    private sealed record QuickActionTemplate(string Label, string Url);

    private sealed record PortalTemplate(
        string Name,
        string Category,
        string IconKey,
        string MainUrl,
        bool PinToRibbon = false,
        QuickActionTemplate[]? QuickActions = null);

    private sealed record SnippetTemplate(
        string Title,
        string Category,
        string Text,
        bool IsPinned = false);

    private static readonly PortalTemplate[] PortalTemplates =
    [
        new("GitHub", "Development", "GH", "https://github.com/", true,
            [new("New repo", "https://github.com/new"), new("Activity", "https://github.com/notifications"), new("Settings", "https://github.com/settings/profile")]),
        new("Microsoft Azure", "Cloud", "AZ", "https://portal.azure.com/", true),
        new("Microsoft Fabric", "Cloud", "FB", "https://app.fabric.microsoft.com/", true),
        new("Databricks", "Cloud", "DB", "https://accounts.cloud.databricks.com/", true),
        new("Vercel", "Deploy", "V", "https://vercel.com/dashboard", true,
            [new("New project", "https://vercel.com/new"), new("Deployments", "https://vercel.com/dashboard"), new("Settings", "https://vercel.com/account/settings")]),
        new("MongoDB Atlas", "Data", "M", "https://cloud.mongodb.com/", true,
            [new("New project", "https://cloud.mongodb.com/"), new("Monitor", "https://cloud.mongodb.com/"), new("Settings", "https://cloud.mongodb.com/")]),
        new("Netlify", "Deploy", "N", "https://app.netlify.com/"),
        new("Cloudflare", "Deploy", "CF", "https://dash.cloudflare.com/"),
        new("ChatGPT", "AI", "AI", "https://chatgpt.com/", true,
            [new("New chat", "https://chatgpt.com/"), new("Projects", "https://chatgpt.com/"), new("Settings", "https://chatgpt.com/")]),
        new("Codex", "AI", "CX", "https://chatgpt.com/codex", true,
            [new("New task", "https://chatgpt.com/codex"), new("Recent", "https://chatgpt.com/codex"), new("Settings", "https://chatgpt.com/")]),
        new("Claude", "AI", "CL", "https://claude.ai/", true,
            [new("New chat", "https://claude.ai/new"), new("Projects", "https://claude.ai/"), new("Settings", "https://claude.ai/settings")]),
        new("LinkedIn", "Work", "in", "https://www.linkedin.com/"),
        new("Google Drive", "Files", "GD", "https://drive.google.com/", true,
            [new("My Drive", "https://drive.google.com/drive/my-drive"), new("Recent", "https://drive.google.com/drive/recent"), new("Settings", "https://drive.google.com/drive/settings")]),
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
                ResourceCatalogService.UrlsEquivalent(portal.MainUrl, template.MainUrl)
                || string.Equals((portal.Name ?? string.Empty).Trim(), template.Name, StringComparison.OrdinalIgnoreCase));

            PortalEntry? portal = portals.FirstOrDefault(item =>
                ResourceCatalogService.UrlsEquivalent(item.MainUrl, template.MainUrl)
                || string.Equals((item.Name ?? string.Empty).Trim(), template.Name, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                portal = new PortalEntry
                {
                    Name = template.Name,
                    Category = template.Category,
                    IconKey = template.IconKey,
                    MainUrl = template.MainUrl,
                    IsPinnedToRibbon = template.PinToRibbon,
                    SortOrder = nextPortalOrder,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                };
                portals.Add(portal);
                nextPortalOrder += 10;
                portalsAdded++;
            }

            if (portal is not null && template.QuickActions is not null)
            {
                portal.QuickActions ??= [];
                int nextActionOrder = portal.QuickActions.Count == 0 ? 10 : portal.QuickActions.Max(item => item.SortOrder) + 10;
                foreach (QuickActionTemplate action in template.QuickActions)
                {
                    if (portal.QuickActions.Any(existing =>
                        string.Equals(existing.Label, action.Label, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    portal.QuickActions.Add(new PortalLinkEntry
                    {
                        Label = action.Label,
                        Url = action.Url,
                        SortOrder = nextActionOrder,
                    });
                    nextActionOrder += 10;
                }
            }
        }

        foreach (SnippetTemplate template in SnippetTemplates)
        {
            bool exists = snippets.Any(snippet =>
                string.Equals((snippet.Title ?? string.Empty).Trim(), template.Title, StringComparison.OrdinalIgnoreCase));

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
