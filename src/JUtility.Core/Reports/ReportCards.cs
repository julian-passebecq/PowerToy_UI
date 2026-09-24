using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JUtility.Core.Actions;

namespace JUtility.Core.Reports;

// V2.1 Mongoku report cards: on-demand, read-only summaries of Mongoku saved reports
// (GET {source}/api/datapass/reports/{reportId}). Power Ops never connects to MongoDB, stores no credential,
// shows section states and row counts only (never row contents) and keeps results in memory only.

public sealed class ReportCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Optional display name; the report's own title is used when empty.</summary>
    public string Title { get; set; } = "";
    /// <summary>Mongoku base address, e.g. http://localhost:3100/.</summary>
    public string SourceUrl { get; set; } = "http://localhost:3100/";
    public string ReportId { get; set; } = "";
}

public sealed class ReportCardSettings
{
    [JsonRequired]
    public string Format { get; set; } = ReportCards.FormatName;
    [JsonRequired]
    public int SchemaVersion { get; set; } = 1;
    public List<ReportCard> Cards { get; set; } = [];
}

public enum SectionHealth
{
    Ok,
    Partial,
    Unavailable,
    Unknown,
}

public sealed record ReportSectionSummary(string Label, string? Authority, string State, int? ReturnedRows, bool Truncated)
{
    public SectionHealth Health => ReportCards.HealthOf(State);
    public string Explanation => ReportCards.Explain(State);
}

public sealed record ReportSummary(
    string ReportId,
    string Title,
    string? Description,
    DateTimeOffset? GeneratedAt,
    bool? ReadOnly,
    IReadOnlyList<ReportSectionSummary> Sections,
    DateTimeOffset FetchedAt)
{
    /// <summary>Worst section wins; a report with no sections is Unknown, never "OK".</summary>
    public SectionHealth Overall => Sections.Count == 0 ? SectionHealth.Unknown : Sections.Max(x => x.Health);
}

public sealed record ReportFetchResult(ReportSummary? Summary, string? Error)
{
    public bool Succeeded => Summary is not null;
}

public sealed record ReportChoice(string Id, string Title, string? Description);

public static partial class ReportCards
{
    public const string FormatName = "powerops-report-cards";
    public const int MaxCards = 8, MaxResponseBytes = 4 * 1024 * 1024, MaxSections = 64;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [GeneratedRegex("^[A-Z0-9_]{1,64}$")]
    private static partial Regex ReportIdPattern();

    public static void Validate(ReportCardSettings settings)
    {
        if (settings is null || settings.Format != FormatName || settings.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported report-cards format/version. Existing files were not rewritten.");
        if (settings.Cards is null || settings.Cards.Count > MaxCards)
            throw new InvalidDataException($"Keep at most {MaxCards} report cards.");
        var ids = new HashSet<Guid>();
        foreach (ReportCard card in settings.Cards)
        {
            if (card is null || card.Id == Guid.Empty || !ids.Add(card.Id)) throw new InvalidDataException("Missing or duplicate report card identifier.");
            if (card.Title is null || card.Title.Length > 80) throw new InvalidDataException("Report card titles are limited to 80 characters.");
            QuickWebApps.ValidateUrl(card.SourceUrl, "Report source");
            if (!ReportIdPattern().IsMatch(card.ReportId ?? "")) throw new InvalidDataException($"'{card.ReportId}' is not a Mongoku report id (capital letters, digits and _).");
        }
    }

    public static Uri ReportUri(ReportCard card) => new(new Uri(EnsureSlash(card.SourceUrl)), "api/datapass/reports/" + Uri.EscapeDataString(card.ReportId));

    public static Uri WorkspaceUri(string sourceUrl) => new(new Uri(EnsureSlash(sourceUrl)), "api/datapass/workspace");

    /// <summary>Mongoku page for a report: FOIL reports have /foil/report/{id}; global projects live under /projects.</summary>
    public static Uri DeepLink(ReportCard card)
    {
        var root = new Uri(EnsureSlash(card.SourceUrl));
        if (card.ReportId.StartsWith("FOIL_", StringComparison.Ordinal)) return new Uri(root, "foil/report/" + Uri.EscapeDataString(card.ReportId));
        if (card.ReportId == "GLOBAL_PROJECTS") return new Uri(root, "projects");
        return root;
    }

    public static SectionHealth HealthOf(string? state) => state?.ToUpperInvariant() switch
    {
        "OK" => SectionHealth.Ok,
        "TRUNCATED" => SectionHealth.Partial,
        "SOURCE_UNBOUND" or "SOURCE_UNAVAILABLE" or "REGISTRY_UNAVAILABLE" or "REGISTRY_AMBIGUOUS" or "NAMESPACE_UNRESOLVED" or "ERROR" or "FAILED" => SectionHealth.Unavailable,
        _ => SectionHealth.Unknown,
    };

    public static string Explain(string? state) => state?.ToUpperInvariant() switch
    {
        "OK" => "complete",
        "TRUNCATED" => "more rows exist than the report limit",
        "SOURCE_UNBOUND" => "the source is not bound in this Mongoku (no data read)",
        "SOURCE_UNAVAILABLE" => "the source database was not reachable",
        "REGISTRY_UNAVAILABLE" => "the FOIL resource registry was not reachable",
        "REGISTRY_AMBIGUOUS" => "the resource registry has several matching entries",
        "NAMESPACE_UNRESOLVED" => "the collection could not be resolved",
        null or "" => "no state reported",
        _ => "state reported by Mongoku",
    };

    /// <summary>Parses a report response defensively: unknown fields are ignored, missing values stay unknown.</summary>
    public static ReportSummary Parse(string json, DateTimeOffset fetchedAt)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Mongoku returned an unexpected report format.");
        string reportId = Text(root, "reportId") ?? "";
        var sections = new List<ReportSectionSummary>();
        if (root.TryGetProperty("sections", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement section in list.EnumerateArray().Take(MaxSections))
            {
                if (section.ValueKind != JsonValueKind.Object) continue;
                JsonElement meta = section.TryGetProperty("meta", out JsonElement m) && m.ValueKind == JsonValueKind.Object ? m : default;
                int? rows = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("returnedRows", out JsonElement r) && r.TryGetInt32(out int n) ? n : null;
                bool truncated = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("truncated", out JsonElement t) && t.ValueKind == JsonValueKind.True;
                string state = (meta.ValueKind == JsonValueKind.Object ? Text(meta, "state") : null) ?? "";
                sections.Add(new ReportSectionSummary(Text(section, "label") ?? Text(section, "id") ?? "Section", Text(section, "authority"), state, rows, truncated));
            }
        }

        DateTimeOffset? generated = Text(root, "generatedAt") is string g && DateTimeOffset.TryParse(g, out DateTimeOffset parsed) ? parsed : null;
        bool? readOnly = root.TryGetProperty("readOnly", out JsonElement ro) && ro.ValueKind is JsonValueKind.True or JsonValueKind.False ? ro.GetBoolean() : null;
        return new ReportSummary(reportId, Text(root, "title") ?? reportId, Text(root, "description"), generated, readOnly, sections.AsReadOnly(), fetchedAt);
    }

    /// <summary>One on-demand GET. Never throws for network/HTTP problems: the card shows the returned message.</summary>
    public static async Task<ReportFetchResult> FetchAsync(ReportCard card, HttpMessageHandler handler, CancellationToken cancellation = default)
    {
        Validate(new ReportCardSettings { Cards = [card] });
        (string? body, string? error) = await GetAsync(ReportUri(card), card.SourceUrl, handler, cancellation).ConfigureAwait(false);
        if (body is null) return new ReportFetchResult(null, error);
        try
        {
            return new ReportFetchResult(Parse(body, DateTimeOffset.Now), null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return new ReportFetchResult(null, "Mongoku returned a report Power Ops could not read: " + ex.Message);
        }
    }

    /// <summary>Report ids and titles from the Mongoku workspace (there is no list endpoint). On demand, for the card editor.</summary>
    public static async Task<(IReadOnlyList<ReportChoice> Reports, string? Error)> ListReportsAsync(string sourceUrl, HttpMessageHandler handler, CancellationToken cancellation = default)
    {
        QuickWebApps.ValidateUrl(sourceUrl, "Report source");
        (string? body, string? error) = await GetAsync(WorkspaceUri(sourceUrl), sourceUrl, handler, cancellation).ConfigureAwait(false);
        if (body is null) return ([], error);
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement.TryGetProperty("workspace", out JsonElement inner) ? inner : document.RootElement;
            var reports = new List<ReportChoice>();
            if (root.TryGetProperty("reports", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement report in list.EnumerateArray())
                {
                    if (Text(report, "id") is string id && ReportIdPattern().IsMatch(id))
                        reports.Add(new ReportChoice(id, Text(report, "title") ?? id, Text(report, "description")));
                }
            }

            return (reports.AsReadOnly(), reports.Count == 0 ? "Mongoku listed no saved reports." : null);
        }
        catch (JsonException ex)
        {
            return ([], "Mongoku returned a workspace Power Ops could not read: " + ex.Message);
        }
    }

    public static ReportCardSettings Defaults() => new();

    private static async Task<(string? Body, string? Error)> GetAsync(Uri uri, string sourceUrl, HttpMessageHandler handler, CancellationToken cancellation)
    {
        string where = new Uri(EnsureSlash(sourceUrl)).GetLeftPart(UriPartial.Authority);
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout, MaxResponseContentBufferSize = MaxResponseBytes };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        try
        {
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            string? body = await ReadBoundedAsync(response.Content, cancellation).ConfigureAwait(false);
            if (body is null) return (null, $"The response from {where} is larger than {MaxResponseBytes / 1024 / 1024} MB; open it in Mongoku instead.");
            if (response.IsSuccessStatusCode) return (body, null);
            return (null, response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    $"Mongoku at {where} requires sign-in. Power Ops does not store credentials; open the report in Mongoku instead.",
                >= HttpStatusCode.Ambiguous and < HttpStatusCode.BadRequest =>
                    $"Mongoku at {where} redirected the request (sign-in page?). Open the report in Mongoku instead.",
                HttpStatusCode.NotFound => $"{where} has no Mongoku report API (is this the datapass/control-plane-v1 Mongoku?).",
                _ => $"Mongoku answered HTTP {(int)response.StatusCode}: {ServerError(body) ?? response.ReasonPhrase}",
            });
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return (null, $"Mongoku at {where} did not answer within {Timeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException)
        {
            return (null, $"Mongoku is not reachable at {where}. Is it running?");
        }
    }

    /// <summary>Reads at most <see cref="MaxResponseBytes"/> (also for chunked responses without Content-Length); null when larger.</summary>
    private static async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken cancellation)
    {
        if (content.Headers.ContentLength > MaxResponseBytes) return null;
        await using Stream stream = await content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string? ServerError(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return Text(document.RootElement, "error");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string EnsureSlash(string url) => url.Trim().EndsWith('/') ? url.Trim() : url.Trim() + "/";
}

public sealed class ReportCardStore
{
    public const int MaxBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = false };
    public string FilePath { get; }

    public ReportCardStore(string directory) => FilePath = Path.Combine(Path.GetFullPath(directory), "report-cards.json");

    /// <summary>Missing file = no cards (nothing written). Malformed/future files fail closed and are preserved.</summary>
    public ReportCardSettings Load() => File.Exists(FilePath) ? Read(FilePath) : ReportCards.Defaults();

    public static ReportCardSettings Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Report cards JSON exceeds 64 KiB.");
        var settings = JsonSerializer.Deserialize<ReportCardSettings>(stream, Json) ?? throw new InvalidDataException("Empty report cards JSON.");
        ReportCards.Validate(settings);
        return settings;
    }

    public void Save(ReportCardSettings settings)
    {
        ReportCards.Validate(settings);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(settings, Json);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (File.Exists(FilePath)) _ = Read(FilePath); // Never overwrite unsupported/corrupt bytes.
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".backup", true);
            else File.Move(temporary, FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
