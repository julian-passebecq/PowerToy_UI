using JUtility.Core.Models;

namespace JUtility.Core.Services;

public static class ProjectClipboardFormatter
{
    public static string FormatRow(ProjectEntry project)
    {
        ArgumentNullException.ThrowIfNull(project);

        List<string> parts = [];
        AddIf(parts, project.CopyName, project.Name);
        AddIf(parts, project.CopyRepo, project.RepoUrl);
        AddIf(parts, project.CopySite, project.SiteUrl);
        AddIf(parts, project.CopyExtra, project.ExtraUrl);
        return string.Join(" ", parts);
    }

    public static string FormatAll(IEnumerable<ProjectEntry> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        return string.Join(
            Environment.NewLine,
            projects
                .Where(project => project.IncludeInCopyAll && !project.IsArchived)
                .Select(FormatRow)
                .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static void AddIf(ICollection<string> parts, bool enabled, string? value)
    {
        if (enabled && !string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value.Trim());
        }
    }
}
