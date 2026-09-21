using JUtility.Core.Models;

namespace JUtility.Core.Services;

public sealed record RepositoryMergeSummary(int Added, int Updated, int Total);

public static class RepositoryCatalogService
{
    public static RepositoryMergeSummary MergeGitHubRepositories(
        IList<ProjectEntry> projects,
        IEnumerable<GitHubRepositorySnapshot> repositories)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(repositories);

        int added = 0;
        int updated = 0;

        foreach (GitHubRepositorySnapshot snapshot in repositories)
        {
            string normalizedUrl = snapshot.Url.TrimEnd('/');
            ProjectEntry? existing = projects.FirstOrDefault(project =>
                (!string.IsNullOrWhiteSpace(project.GitHubFullName)
                    && string.Equals(project.GitHubFullName, snapshot.FullName, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(project.RepoUrl)
                    && string.Equals(project.RepoUrl.TrimEnd('/'), normalizedUrl, StringComparison.OrdinalIgnoreCase)));

            if (existing is null)
            {
                string family = DetectProjectFamily(snapshot.Name);
                projects.Add(new ProjectEntry
                {
                    Name = snapshot.Name,
                    Category = family,
                    Subcategory = DetectProjectSubcategory(family, snapshot.Name),
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
                });
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

        return new RepositoryMergeSummary(added, updated, projects.Count);
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
}
