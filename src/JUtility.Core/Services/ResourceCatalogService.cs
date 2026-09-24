using JUtility.Core.Models;

namespace JUtility.Core.Services;

public sealed record ResourceUrlClassification(
    string Provider,
    string Kind,
    string SuggestedName,
    string NormalizedUrl);

public sealed record ResourceUpsertResult(
    WorkspaceResourceEntry Resource,
    bool Added);

public sealed record ResourceImportSummary(
    int Added,
    int Updated,
    int Total);

public static class ResourceCatalogService
{
    public static bool TryClassify(string? value, out ResourceUrlClassification classification)
    {
        classification = new ResourceUrlClassification("Other", "Link", "New resource", string.Empty);

        if (!UrlNormalizer.TryNormalizeOptionalWebUrl(value, out string normalized)
            || string.IsNullOrWhiteSpace(normalized)
            || !Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        string provider = DetectProvider(uri);
        string kind = DetectKind(uri, provider);
        string suggestedName = DetectSuggestedName(uri, provider, kind);

        classification = new ResourceUrlClassification(provider, kind, suggestedName, normalized);
        return true;
    }

    public static ResourceUpsertResult UpsertUrl(
        IList<WorkspaceResourceEntry> resources,
        string url,
        string? group = null)
    {
        ArgumentNullException.ThrowIfNull(resources);

        if (!TryClassify(url, out ResourceUrlClassification classification))
        {
            throw new ArgumentException("A valid HTTP or HTTPS resource URL is required.", nameof(url));
        }

        WorkspaceResourceEntry? existing = resources.FirstOrDefault(resource =>
            UrlsEquivalent(resource.Url, classification.NormalizedUrl));

        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(existing.Provider) || existing.Provider.Equals("Other", StringComparison.OrdinalIgnoreCase))
            {
                existing.Provider = classification.Provider;
            }

            if (string.IsNullOrWhiteSpace(existing.Kind) || existing.Kind.Equals("Link", StringComparison.OrdinalIgnoreCase))
            {
                existing.Kind = classification.Kind;
            }

            if (string.IsNullOrWhiteSpace(existing.Name) || existing.Name.Equals("New resource", StringComparison.OrdinalIgnoreCase))
            {
                existing.Name = classification.SuggestedName;
            }

            if (!string.IsNullOrWhiteSpace(group) && (string.IsNullOrWhiteSpace(existing.Group) || existing.Group.Equals("General", StringComparison.OrdinalIgnoreCase)))
            {
                existing.Group = group.Trim();
            }

            existing.Url = classification.NormalizedUrl;
            existing.UpdatedUtc = DateTimeOffset.UtcNow;
            return new ResourceUpsertResult(existing, Added: false);
        }

        WorkspaceResourceEntry created = new()
        {
            Name = classification.SuggestedName,
            Provider = classification.Provider,
            Kind = classification.Kind,
            Group = string.IsNullOrWhiteSpace(group) ? "General" : group.Trim(),
            Url = classification.NormalizedUrl,
            SortOrder = resources.Count == 0 ? 10 : resources.Max(resource => resource.SortOrder) + 10,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };

        resources.Add(created);
        return new ResourceUpsertResult(created, Added: true);
    }

    public static ResourceImportSummary ImportProjects(
        IList<WorkspaceResourceEntry> resources,
        IEnumerable<ProjectEntry> projects)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(projects);

        int added = 0;
        int updated = 0;

        foreach (ProjectEntry project in projects.Where(project => !project.IsArchived && !string.IsNullOrWhiteSpace(project.RepoUrl)))
        {
            if (!UrlNormalizer.TryNormalizeOptionalWebUrl(project.RepoUrl, out string normalized)
                || string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            WorkspaceResourceEntry? existing = resources.FirstOrDefault(resource =>
                resource.SourceProjectId == project.Id || UrlsEquivalent(resource.Url, normalized));

            if (existing is null)
            {
                WorkspaceResourceEntry created = new()
                {
                    Name = project.Name,
                    Provider = "GitHub",
                    Kind = "Repository",
                    Group = string.IsNullOrWhiteSpace(project.Category) ? "Projects" : project.Category,
                    Url = normalized,
                    SourceProjectId = project.Id,
                    SortOrder = resources.Count == 0 ? 10 : resources.Max(resource => resource.SortOrder) + 10,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                };
                resources.Add(created);
                added++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(existing.Name)
                || existing.Name.Equals("New resource", StringComparison.OrdinalIgnoreCase)
                || existing.Name.Equals("Untitled resource", StringComparison.OrdinalIgnoreCase))
            {
                existing.Name = project.Name;
            }

            if (string.IsNullOrWhiteSpace(existing.Group)
                || existing.Group.Equals("General", StringComparison.OrdinalIgnoreCase))
            {
                existing.Group = string.IsNullOrWhiteSpace(project.Category) ? "Projects" : project.Category;
            }

            existing.Provider = "GitHub";
            existing.Kind = "Repository";
            existing.Url = normalized;
            existing.SourceProjectId = project.Id;
            existing.UpdatedUtc = DateTimeOffset.UtcNow;
            updated++;
        }

        return new ResourceImportSummary(added, updated, resources.Count);
    }

    public static bool MatchesFilter(
        WorkspaceResourceEntry resource,
        IReadOnlySet<string>? activeProviders,
        string? filter,
        string? search)
    {
        ArgumentNullException.ThrowIfNull(resource);

        string normalizedSearch = search?.Trim() ?? string.Empty;
        if (normalizedSearch.Length > 0)
        {
            string haystack = string.Join(
                " ",
                resource.Name,
                resource.Provider,
                resource.Kind,
                resource.Group,
                resource.Url,
                resource.Note);

            if (!haystack.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (activeProviders is { Count: > 0 } && !activeProviders.Contains(resource.Provider))
        {
            return false;
        }

        string normalizedFilter = string.IsNullOrWhiteSpace(filter) ? "all" : filter.Trim();
        if (normalizedFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedFilter.Equals("favorites", StringComparison.OrdinalIgnoreCase))
        {
            return resource.IsFavorite;
        }

        if (normalizedFilter.Equals("pinned", StringComparison.OrdinalIgnoreCase))
        {
            return resource.IsPinned;
        }

        if (normalizedFilter.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(
                resource.Group,
                normalizedFilter["group:".Length..],
                StringComparison.OrdinalIgnoreCase);
        }

        // Provider names are used directly as secondary-navigation filter keys.
        return string.Equals(resource.Provider, normalizedFilter, StringComparison.OrdinalIgnoreCase);
    }

    public static bool UrlsEquivalent(string? left, string? right)
    {
        if (!UrlNormalizer.TryNormalizeOptionalWebUrl(left, out string normalizedLeft)
            || !UrlNormalizer.TryNormalizeOptionalWebUrl(right, out string normalizedRight)
            || string.IsNullOrWhiteSpace(normalizedLeft)
            || string.IsNullOrWhiteSpace(normalizedRight)
            || !Uri.TryCreate(normalizedLeft, UriKind.Absolute, out Uri? leftUri)
            || !Uri.TryCreate(normalizedRight, UriKind.Absolute, out Uri? rightUri))
        {
            return false;
        }

        if (!string.Equals(leftUri.Scheme, rightUri.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(leftUri.Host, rightUri.Host, StringComparison.OrdinalIgnoreCase)
            || leftUri.Port != rightUri.Port)
        {
            return false;
        }

        string leftPath = CanonicalPath(leftUri);
        string rightPath = CanonicalPath(rightUri);
        StringComparison pathComparison =
            leftUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        return string.Equals(leftPath, rightPath, pathComparison)
            && string.Equals(leftUri.Query, rightUri.Query, StringComparison.Ordinal)
            && string.Equals(leftUri.Fragment, rightUri.Fragment, StringComparison.Ordinal);
    }

    private static string CanonicalPath(Uri uri)
    {
        string path = uri.AbsolutePath;
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    private static string DetectProvider(Uri uri)
    {
        string host = uri.Host.ToLowerInvariant();

        if (host == "github.com" || host.EndsWith(".github.com", StringComparison.Ordinal)) return "GitHub";
        if (host == "drive.google.com" || host == "docs.google.com") return "Google Drive";
        if (host == "dropbox.com" || host.EndsWith(".dropbox.com", StringComparison.Ordinal)) return "Dropbox";
        if (host == "onedrive.live.com" || host.EndsWith(".onedrive.live.com", StringComparison.Ordinal) || host == "1drv.ms") return "OneDrive";
        if (host.EndsWith(".sharepoint.com", StringComparison.Ordinal)) return "SharePoint";
        if (host == "notion.so" || host.EndsWith(".notion.so", StringComparison.Ordinal)
            || host == "notion.site" || host.EndsWith(".notion.site", StringComparison.Ordinal)) return "Notion";

        return uri.Host;
    }

    private static string DetectKind(Uri uri, string provider)
    {
        string path = uri.AbsolutePath.ToLowerInvariant();

        return provider switch
        {
            "GitHub" => path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 2 ? "Repository" : "Link",
            "Google Drive" when uri.Host.Equals("docs.google.com", StringComparison.OrdinalIgnoreCase) => "Document",
            "Google Drive" when path.Contains("/folders/", StringComparison.Ordinal) => "Folder",
            "Google Drive" when path.Contains("/file/", StringComparison.Ordinal) => "Document",
            "Dropbox" when path.Contains("/home", StringComparison.Ordinal)
                || path.Contains("/sh/", StringComparison.Ordinal)
                || path.Contains("/scl/fo/", StringComparison.Ordinal) => "Folder",
            "Dropbox" when path.Contains("/scl/fi/", StringComparison.Ordinal) => "Document",
            "OneDrive" => "Folder",
            "SharePoint" => "Folder",
            "Notion" => "Page",
            _ => "Link",
        };
    }

    private static string DetectSuggestedName(Uri uri, string provider, string kind)
    {
        string[] segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        if (provider == "GitHub" && segments.Length >= 2)
        {
            return segments[1];
        }

        if (provider is "Google Drive" or "Dropbox" or "OneDrive" or "SharePoint" or "Notion")
        {
            return $"{provider} {kind}";
        }

        string? candidate = segments
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment)
                && !segment.Equals("view", StringComparison.OrdinalIgnoreCase)
                && !segment.Equals("edit", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length <= 80)
        {
            return candidate;
        }

        return $"{provider} {kind}";
    }
}
