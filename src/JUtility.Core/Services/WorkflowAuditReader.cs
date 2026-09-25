using System.Globalization;
using System.Text.RegularExpressions;

namespace JUtility.Core.Services;

public enum WorkflowAuditStatus
{
    Unknown = 0,
    Ok = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>One workflow-audit report (audits/&lt;YYYY-MM-DD&gt;-&lt;matin|soir&gt;.md).</summary>
public sealed record WorkflowAuditReport(
    string FilePath,
    DateOnly Date,
    string Slot,
    DateTimeOffset RunAt,
    WorkflowAuditStatus Status,
    IReadOnlyList<string> Summary,
    int ProblemCount,
    string Markdown)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(14);

    public bool IsStale(DateTimeOffset now) => now - RunAt > StaleAfter;
}

/// <summary>
/// Read-only view of the reports written by the "workflow-audit" Claude scheduled task.
/// Never writes to the audits folder.
/// </summary>
public sealed partial class WorkflowAuditReader
{
    public const string ScheduledTaskName = "workflow-audit";

    /// <summary>Opens the task page in the Claude desktop app. The app exposes no "run now" URL or CLI.</summary>
    public const string ScheduledTaskDeepLink = "claude://claude.ai/epitaxy/scheduled/" + ScheduledTaskName;

    public WorkflowAuditReader(string auditsDirectory)
    {
        AuditsDirectory = auditsDirectory;
    }

    public string AuditsDirectory { get; }

    /// <summary>%USERPROFILE%\.claude\effort-board\audits, or JUTILITY_AUDITS_DIR when set (fixtures).</summary>
    public static WorkflowAuditReader CreateDefault()
    {
        string? overrideDir = Environment.GetEnvironmentVariable("JUTILITY_AUDITS_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return new WorkflowAuditReader(overrideDir);
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new WorkflowAuditReader(Path.Combine(home, ".claude", "effort-board", "audits"));
    }

    public WorkflowAuditReport? ReadLatest()
    {
        if (!Directory.Exists(AuditsDirectory))
        {
            return null;
        }

        var latest = Directory.EnumerateFiles(AuditsDirectory, "*.md")
            .Select(path => (Path: path, Key: ParseFileName(Path.GetFileName(path))))
            .Where(f => f.Key is not null)
            .OrderByDescending(f => f.Key!.Value.Date)
            .ThenByDescending(f => SlotOrder(f.Key!.Value.Slot))
            .FirstOrDefault();
        if (latest.Path is null)
        {
            return null;
        }

        string markdown = ReadShared(latest.Path);
        DateTimeOffset runAt = new(File.GetLastWriteTimeUtc(latest.Path), TimeSpan.Zero);
        return Parse(latest.Path, latest.Key!.Value.Date, latest.Key.Value.Slot, runAt, markdown);
    }

    /// <summary>Last non-empty line of log.md (one line per run), or null.</summary>
    public string? ReadLastLogLine()
    {
        string path = Path.Combine(AuditsDirectory, "log.md");
        if (!File.Exists(path))
        {
            return null;
        }

        return ReadShared(path)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => !line.StartsWith('#'));
    }

    public static (DateOnly Date, string Slot)? ParseFileName(string fileName)
    {
        Match match = FileNamePattern().Match(fileName);
        if (!match.Success
            || !DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            return null;
        }

        return (date, match.Groups[2].Value.ToLowerInvariant());
    }

    public static WorkflowAuditReport Parse(string filePath, DateOnly date, string slot, DateTimeOffset runAt, string markdown)
    {
        Dictionary<string, List<string>> sections = SplitSections(markdown);
        List<string> resume = FindSection(sections, "resume") ?? [];
        List<string> problems = FindSection(sections, "problemes") ?? [];

        WorkflowAuditStatus status = StatusOf(string.Join('\n', resume));
        if (status == WorkflowAuditStatus.Unknown)
        {
            status = StatusOf(markdown);
        }

        List<string> summary = resume
            .Select(CleanLine)
            .Where(line => line.Length > 0)
            .Take(3)
            .ToList();

        return new WorkflowAuditReport(filePath, date, slot, runAt, status, summary, CountProblems(problems), markdown);
    }

    public static WorkflowAuditStatus StatusOf(string text)
    {
        // Worst emoji wins so a 🔴 anywhere in the summary is never hidden by a 🟢.
        if (text.Contains("🔴", StringComparison.Ordinal)) return WorkflowAuditStatus.Critical;
        if (text.Contains("🟠", StringComparison.Ordinal)) return WorkflowAuditStatus.Warning;
        if (text.Contains("🟢", StringComparison.Ordinal)) return WorkflowAuditStatus.Ok;
        return WorkflowAuditStatus.Unknown;
    }

    /// <summary>Top-level list items or ### sub-headings; "aucun"/"none" alone means zero.</summary>
    public static int CountProblems(IReadOnlyList<string> lines)
    {
        int headings = lines.Count(line => line.TrimStart().StartsWith("###", StringComparison.Ordinal));
        if (headings > 0)
        {
            return headings;
        }

        return lines.Count(line =>
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
            {
                return false; // Nested details belong to the problem above.
            }

            Match item = ListItemPattern().Match(line);
            if (!item.Success)
            {
                return false;
            }

            string rest = Normalize(line[item.Length..]).Trim(' ', '.', '*', '_');
            return rest is not ("aucun" or "aucun probleme" or "aucun probleme detecte" or "none" or "rien a signaler" or "ras");
        });
    }

    private static Dictionary<string, List<string>> SplitSections(string markdown)
    {
        Dictionary<string, List<string>> sections = new(StringComparer.Ordinal);
        List<string>? current = null;
        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            Match heading = SectionHeadingPattern().Match(raw);
            if (heading.Success)
            {
                current = [];
                sections.TryAdd(Normalize(heading.Groups[1].Value), current);
                continue;
            }

            current?.Add(raw.TrimEnd());
        }

        return sections;
    }

    private static List<string>? FindSection(Dictionary<string, List<string>> sections, string prefix) =>
        sections.FirstOrDefault(s => s.Key.Contains(prefix, StringComparison.Ordinal)).Value;

    private static string CleanLine(string line)
    {
        string text = ListItemPattern().Replace(line.Trim(), "");
        return text.Replace("**", "", StringComparison.Ordinal).Trim();
    }

    /// <summary>Lower case, accents stripped, so "Résumé" and "Problèmes détectés" match plainly.</summary>
    private static string Normalize(string text)
    {
        string decomposed = text.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static int SlotOrder(string slot) => slot == "soir" ? 1 : 0;

    private static string ReadShared(string path)
    {
        // The audit task may be writing the file while we read it.
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})-(matin|soir)\.md$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"^#{1,2}\s+(.+?)\s*#*\s*$")]
    private static partial Regex SectionHeadingPattern();

    [GeneratedRegex(@"^\s*(?:[-*+]|\d+[.)])\s+")]
    private static partial Regex ListItemPattern();
}
