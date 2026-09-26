using System.Text.Json;

namespace JUtility.Core.Reports;

// Mongoku's global read-only MAINTENANCE report (GET {source}/api/datapass/reports/MAINTENANCE, page {source}/maintenance).
// Unlike other report cards, this card shows a few row fields, because the rows ARE the maintenance advice: the summary
// counts plus, for rows that need something, title / short summary / next-action label / Mongoku link. Nothing else from a
// row is kept (no repository names, heads or ids beyond the link), nothing is executed and nothing is persisted.

/// <param name="Section">Id of the section the row came from (projects, heads, audits...).</param>
/// <param name="OpenUri">Absolute page inside the same Mongoku (from the row's openUri), or null to use the maintenance page.</param>
public sealed record MaintenanceRow(string Section, string Title, string? Summary, string ActionKind, string NextAction, Uri? OpenUri);

/// <param name="Unavailable">trace.resolved was false or the section state is an unavailable state: its data is not shown.</param>
public sealed record MaintenanceSection(string Id, string Label, bool Unavailable, string State, int ActionRows);

public sealed record MaintenanceDigest(
    string? SummaryLine,
    string? NextAction,
    string? SourcesReachable,
    int? ProjectsNeedingAction,
    int? HeadsToReconcile,
    int? AuditsToReview,
    int? ProjectionsNeedingAction,
    int? ReconciliationFindings,
    string? Backups,
    IReadOnlyList<MaintenanceSection> Sections,
    IReadOnlyList<MaintenanceRow> Actions)
{
    public bool SummaryUnavailable => Sections.FirstOrDefault(x => x.Id == "summary") is not { Unavailable: false };

    public IEnumerable<MaintenanceSection> UnavailableSections => Sections.Where(x => x.Unavailable);

    /// <summary>Count to display for a section, or null when that section's source is unavailable (never a substitute number).</summary>
    public int? CountFor(string sectionId, int? reported) =>
        Sections.FirstOrDefault(x => x.Id == sectionId) is { Unavailable: true } || SummaryUnavailable ? null : reported;
}

public static class MaintenanceReport
{
    public const string ReportId = "MAINTENANCE";
    public const int MaxActions = 64, MaxText = 300;

    public static bool Is(string? reportId) => string.Equals(reportId, ReportId, StringComparison.Ordinal);

    /// <summary>Mongoku's page for the report, relative to the card's Mongoku address.</summary>
    public static Uri Page(Uri mongokuRoot) => new(mongokuRoot, "maintenance");

    /// <summary>
    /// Resolves a row's openUri (e.g. "/?project=atlas") against the Mongoku base address. Only same-origin paths
    /// under that base are accepted; anything else (other hosts, schemes, "//host", backslashes) returns null.
    /// </summary>
    public static Uri? ResolveOpenUri(Uri? mongokuRoot, string? openUri)
    {
        if (mongokuRoot is null || string.IsNullOrWhiteSpace(openUri) || openUri.Length > 512) return null;
        string value = openUri.Trim();
        if (!value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal) || value.Contains('\\') || value.Any(char.IsControl)) return null;
        if (!Uri.TryCreate(mongokuRoot, value[1..], out Uri? resolved)) return null;
        bool sameOrigin = resolved.Scheme == mongokuRoot.Scheme
            && string.Equals(resolved.Authority, mongokuRoot.Authority, StringComparison.OrdinalIgnoreCase)
            && resolved.AbsolutePath.StartsWith(mongokuRoot.AbsolutePath, StringComparison.Ordinal);
        return sameOrigin ? resolved : null;
    }

    /// <summary>One line of counts; a count whose section (or the summary) is unavailable reads "unavailable", never 0.</summary>
    public static string CountsLine(MaintenanceDigest digest)
    {
        static string Count(int? n) => n is int value ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unavailable";
        bool sourcesDown = digest.SummaryUnavailable || digest.Sections.FirstOrDefault(x => x.Id == "sources") is { Unavailable: true };
        string backups = digest.SummaryUnavailable ? "unavailable" : digest.Backups switch
        {
            null => "unknown",
            "NOT_RECORDED" => "not recorded",
            string b => b.ToLowerInvariant().Replace('_', ' '),
        };
        return $"Sources {(sourcesDown ? "unavailable" : digest.SourcesReachable ?? "unknown")}"
            + $" · projects {Count(digest.CountFor("projects", digest.ProjectsNeedingAction))}"
            + $" · heads {Count(digest.CountFor("heads", digest.HeadsToReconcile))}"
            + $" · projections {Count(digest.CountFor("projections", digest.ProjectionsNeedingAction))}"
            + $" · audits {Count(digest.CountFor("audits", digest.AuditsToReview))}"
            + $" · reconciliation {Count(digest.CountFor("reconciliation", digest.ReconciliationFindings))}"
            + $" · backups {backups}";
    }

    /// <summary>Reads the MAINTENANCE-specific fields. Defensive: unknown or wrongly typed values stay null, never throws on shape.</summary>
    public static MaintenanceDigest Parse(JsonElement root, Uri? mongokuRoot)
    {
        var sections = new List<MaintenanceSection>();
        var actions = new List<MaintenanceRow>();
        JsonElement summaryRow = default;
        if (root.TryGetProperty("sections", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement section in list.EnumerateArray().Take(ReportCards.MaxSections))
            {
                if (section.ValueKind != JsonValueKind.Object) continue;
                string id = Text(section, "id") ?? "";
                string label = Text(section, "label") ?? (id.Length > 0 ? id : "Section");
                string state = section.TryGetProperty("meta", out JsonElement meta) ? Text(meta, "state") ?? "" : "";
                bool unresolved = section.TryGetProperty("trace", out JsonElement trace) && trace.ValueKind == JsonValueKind.Object
                    && trace.TryGetProperty("resolved", out JsonElement resolved) && resolved.ValueKind == JsonValueKind.False;
                bool unavailable = unresolved || ReportCards.HealthOf(state) == SectionHealth.Unavailable;
                int actionRows = 0;
                if (!unavailable && section.TryGetProperty("rows", out JsonElement rows) && rows.ValueKind == JsonValueKind.Array)
                {
                    if (id == "summary")
                    {
                        summaryRow = rows.EnumerateArray().FirstOrDefault(x => x.ValueKind == JsonValueKind.Object);
                    }
                    else
                    {
                        foreach (JsonElement row in rows.EnumerateArray())
                        {
                            string kind = Text(row, "actionKind") ?? "";
                            if (kind.Length == 0 || kind.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                            actionRows++;
                            if (actions.Count >= MaxActions) continue;
                            actions.Add(new MaintenanceRow(
                                id,
                                Clip(Text(row, "title")) ?? "Untitled",
                                Clip(Text(row, "summary")),
                                Clip(kind)!,
                                Clip(Text(row, "nextAction")) ?? kind.Replace('_', ' '),
                                ResolveOpenUri(mongokuRoot, Text(row, "openUri"))));
                        }
                    }
                }

                sections.Add(new MaintenanceSection(id, Clip(label)!, unavailable, state, actionRows));
            }
        }

        bool hasSummary = summaryRow.ValueKind == JsonValueKind.Object;
        return new MaintenanceDigest(
            hasSummary ? Clip(Text(summaryRow, "summary")) : null,
            hasSummary ? Clip(Text(summaryRow, "nextAction")) : null,
            hasSummary ? Clip(Text(summaryRow, "sourcesReachable")) : null,
            hasSummary ? Int(summaryRow, "projectsNeedingAction") : null,
            hasSummary ? Int(summaryRow, "headsToReconcile") : null,
            hasSummary ? Int(summaryRow, "auditsToReview") : null,
            hasSummary ? Int(summaryRow, "projectionsNeedingAction") : null,
            hasSummary ? Int(summaryRow, "reconciliationFindings") : null,
            hasSummary ? Clip(Text(summaryRow, "backups")) : null,
            sections.AsReadOnly(),
            actions.AsReadOnly());
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) && n >= 0
            ? n
            : null;

    private static string? Clip(string? text)
    {
        if (text is null) return null;
        string trimmed = text.Trim();
        return trimmed.Length > MaxText ? trimmed[..(MaxText - 3)] + "..." : trimmed;
    }
}
