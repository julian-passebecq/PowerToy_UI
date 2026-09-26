using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JUtility.Core.Reports;

// Read-only view of the Effort Board's local usage.json (%USERPROFILE%\.claude\effort-board\usage.json),
// produced by the user's own morning job. Power Ops only reads it on demand and never writes to that folder.

public sealed record ClaudeUsageWindow(string Label, double PercentUsed, DateTimeOffset? ResetsAt);

public sealed record ClaudeUsageProject(string Name, long Week, long Today);

public sealed record ClaudeUsageSnapshot(
    string PlanName,
    string Generated,
    string AsOf,
    IReadOnlyList<ClaudeUsageWindow> Windows,
    IReadOnlyList<ClaudeUsageProject> Projects);

public static class ClaudeUsage
{
    public const int MaxBytes = 1024 * 1024, MaxWindows = 10, MaxProjects = 200;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "effort-board", "usage.json");

    public static ClaudeUsageSnapshot Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxBytes) throw new InvalidDataException("usage.json is larger than 1 MiB; not read.");
        using var document = JsonDocument.Parse(stream);
        return Parse(document.RootElement);
    }

    public static ClaudeUsageSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }

    private static ClaudeUsageSnapshot Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("usage.json is not a JSON object.");
        var windows = new List<ClaudeUsageWindow>();
        string planName = "", asOf = "";
        if (root.TryGetProperty("plan", out JsonElement plan) && plan.ValueKind == JsonValueKind.Object)
        {
            planName = Text(plan, "name");
            asOf = Text(plan, "asOf");
            if (plan.TryGetProperty("windows", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement w in list.EnumerateArray().Take(MaxWindows))
                {
                    if (w.ValueKind != JsonValueKind.Object) continue;
                    double percent = w.TryGetProperty("percentUsed", out JsonElement p) && p.TryGetDouble(out double v) ? Math.Clamp(v, 0, 100) : double.NaN;
                    DateTimeOffset? resets = DateTimeOffset.TryParse(Text(w, "resetsAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset r) ? r : null;
                    windows.Add(new ClaudeUsageWindow(RepairMojibake(Text(w, "label")), percent, resets));
                }
            }
        }

        var projects = new List<ClaudeUsageProject>();
        if (root.TryGetProperty("projects", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray().Take(MaxProjects))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string name = Text(item, "name");
                if (name.Length == 0) continue;
                projects.Add(new ClaudeUsageProject(name, Number(item, "week"), Number(item, "today")));
            }
        }

        return new ClaudeUsageSnapshot(planName, Text(root, "generated"), asOf, windows, projects.OrderByDescending(x => x.Week).ToList());
    }

    /// <summary>1_511_041_581 -> "1.51 B"; 905_514_715 -> "906 M"; 12_300 -> "12.3 K".</summary>
    public static string Tokens(long value) => value switch
    {
        >= 1_000_000_000 => (value / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + " B",
        >= 1_000_000 => (value / 1e6).ToString("0", CultureInfo.InvariantCulture) + " M",
        >= 1_000 => (value / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + " K",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Undoes UTF-8 text that was decoded as Windows-1252 once or twice (e.g. "Weekly Ã‚Â· all models" -> "Weekly · all models").
    /// Returns the input unchanged when it does not look double-encoded or the repair would lose characters.
    /// </summary>
    public static string RepairMojibake(string text)
    {
        Encoding cp1252;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            cp1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException) { return text; }

        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        string current = text;
        for (int i = 0; i < 3 && (current.Contains('Ã') || current.Contains('Â')); i++)
        {
            try { current = strict.GetString(cp1252.GetBytes(current)); }
            catch (Exception ex) when (ex is EncoderFallbackException or DecoderFallbackException or ArgumentException) { return i == 0 ? text : current; }
        }
        return current;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? (value.GetString() ?? "").Trim() : "";

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long n) ? Math.Max(0, n) : 0;
}
