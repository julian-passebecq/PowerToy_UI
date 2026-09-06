using System.Text.RegularExpressions;
using JUtility.Core.Models;

namespace JUtility.Core.Services;

public static partial class PromptComposer
{
    public static string Compose(
        IEnumerable<PromptModuleEntry> modules,
        ProjectEntry? project = null,
        IReadOnlyDictionary<string, string>? variables = null,
        bool appendProjectLinks = false)
    {
        ArgumentNullException.ThrowIfNull(modules);

        Dictionary<string, string> replacements = new(StringComparer.OrdinalIgnoreCase);
        if (variables is not null)
        {
            foreach ((string key, string value) in variables)
            {
                replacements[key] = value;
            }
        }

        if (project is not null)
        {
            replacements["project"] = project.Name;
            replacements["repo"] = project.RepoUrl;
            replacements["site"] = project.SiteUrl;
            replacements["extra"] = project.ExtraUrl;
        }

        List<string> sections = modules
            .Where(module => module.IsEnabled && !string.IsNullOrWhiteSpace(module.Body))
            .OrderBy(module => module.SortOrder)
            .ThenBy(module => module.Title, StringComparer.OrdinalIgnoreCase)
            .Select(module => ReplaceVariables(module.Body.Trim(), replacements))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        if (appendProjectLinks && project is not null)
        {
            string projectLine = ProjectClipboardFormatter.FormatRow(project);
            if (!string.IsNullOrWhiteSpace(projectLine))
            {
                sections.Add("Project context:\n" + projectLine);
            }
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    public static IReadOnlyList<string> FindVariables(IEnumerable<PromptModuleEntry> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        return modules
            .Where(module => module.IsEnabled)
            .SelectMany(module => VariableRegex().Matches(module.Body).Cast<Match>().Select(match => match.Groups[1].Value.Trim()))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ReplaceVariables(string text, IReadOnlyDictionary<string, string> variables)
    {
        return VariableRegex().Replace(text, match =>
        {
            string key = match.Groups[1].Value.Trim();
            return variables.TryGetValue(key, out string? value) ? value : match.Value;
        });
    }

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariableRegex();
}
