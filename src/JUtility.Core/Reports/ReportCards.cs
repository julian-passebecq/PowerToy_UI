using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JUtility.Core.Actions;

namespace JUtility.Core.Reports;

// V2.1 Mongoku report cards: on-demand, read-only summaries of Mongoku saved reports
// (GET {source}/api/datapass/reports/{reportId}). Power Ops never connects to MongoDB and never holds a Mongo URI,
// Atlas id or database credential (an optional Mongoku HTTP sign-in lives only in Windows Credential Manager),
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

/// <param name="Resolved">trace.resolved when the report says whether the section's source resolved (e.g. SOURCE_INVENTORY).</param>
public sealed record ReportSectionSummary(string Label, string? Authority, string State, int? ReturnedRows, bool Truncated, bool? Resolved = null)
{
    public SectionHealth Health => ReportCards.HealthOf(State);
    public string Explanation => ReportCards.Explain(State);
}

/// <param name="AuthorityBoundaries">
/// Distinct row-level <c>authorityBoundary</c> markers declared by Mongoku (e.g. NON_AUTHORITATIVE_AI_REASONING).
/// Only these markers are read from rows; row contents are never kept.
/// </param>
public sealed record ReportSummary(
    string ReportId,
    string Title,
    string? Description,
    DateTimeOffset? GeneratedAt,
    bool? ReadOnly,
    IReadOnlyList<ReportSectionSummary> Sections,
    DateTimeOffset FetchedAt,
    IReadOnlyList<string>? AuthorityBoundaries = null)
{
    /// <summary>Mongoku declared at least one row non-authoritative: the card must say so, whatever its title.</summary>
    public bool NonAuthoritative => AuthorityBoundaries?.Any(x => x.StartsWith("NON_AUTHORITATIVE", StringComparison.OrdinalIgnoreCase)) == true;

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

    /// <summary>
    /// Mongoku page for a report. Verified against a live Mongoku (2026-09-25): /foil/report/{id} serves every saved
    /// report (including SOURCE_INVENTORY) except GLOBAL_PROJECTS, which answers 404 there and lives under /projects.
    /// </summary>
    public static Uri DeepLink(ReportCard card)
    {
        var root = new Uri(EnsureSlash(card.SourceUrl));
        return card.ReportId == "GLOBAL_PROJECTS"
            ? new Uri(root, "projects")
            : new Uri(root, "foil/report/" + Uri.EscapeDataString(card.ReportId));
    }

    public static SectionHealth HealthOf(string? state) => state?.ToUpperInvariant() switch
    {
        "OK" or "EMPTY" => SectionHealth.Ok,
        "TRUNCATED" => SectionHealth.Partial,
        "SOURCE_UNBOUND" or "REGISTERED_UNBOUND" or "SOURCE_UNAVAILABLE" or "SOURCE_ERROR" or "REGISTRY_UNAVAILABLE" or "REGISTRY_AMBIGUOUS"
            or "NAMESPACE_UNRESOLVED" or "ERROR" or "FAILED" => SectionHealth.Unavailable,
        _ => SectionHealth.Unknown,
    };

    public static string Explain(string? state) => state?.ToUpperInvariant() switch
    {
        "OK" => "complete",
        "EMPTY" => "reachable, nothing to list",
        "REGISTERED_UNBOUND" => "registered in the resource registry but not bound in this Mongoku",
        "SOURCE_ERROR" => "the source answered with an error",
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
        var boundaries = new SortedSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("sections", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement section in list.EnumerateArray().Take(MaxSections))
            {
                if (section.ValueKind != JsonValueKind.Object) continue;
                if (section.TryGetProperty("rows", out JsonElement sectionRows) && sectionRows.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement row in sectionRows.EnumerateArray())
                    {
                        if (Text(row, "authorityBoundary") is string boundary && boundary.Length <= 80 && boundaries.Count < 8) boundaries.Add(boundary);
                    }
                }

                JsonElement meta = section.TryGetProperty("meta", out JsonElement m) && m.ValueKind == JsonValueKind.Object ? m : default;
                int? rows = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("returnedRows", out JsonElement r) && r.TryGetInt32(out int n) ? n : null;
                bool truncated = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("truncated", out JsonElement t) && t.ValueKind == JsonValueKind.True;
                string state = (meta.ValueKind == JsonValueKind.Object ? Text(meta, "state") : null) ?? "";
                bool? resolved = section.TryGetProperty("trace", out JsonElement trace) && trace.ValueKind == JsonValueKind.Object
                    && trace.TryGetProperty("resolved", out JsonElement res) && res.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? res.GetBoolean()
                        : null;
                string label = Text(section, "label") ?? Text(section, "sourceId") ?? Text(section, "id") ?? "Section";
                sections.Add(new ReportSectionSummary(label, Text(section, "authority"), state, rows, truncated, resolved));
            }
        }

        DateTimeOffset? generated = Text(root, "generatedAt") is string g && DateTimeOffset.TryParse(g, out DateTimeOffset parsed) ? parsed : null;
        bool? readOnly = root.TryGetProperty("readOnly", out JsonElement ro) && ro.ValueKind is JsonValueKind.True or JsonValueKind.False ? ro.GetBoolean() : null;
        return new ReportSummary(reportId, Text(root, "title") ?? reportId, Text(root, "description"), generated, readOnly, sections.AsReadOnly(), fetchedAt, boundaries.ToList().AsReadOnly());
    }

    /// <summary>One on-demand GET. Never throws for network/HTTP problems: the card shows the returned message.</summary>
    public static async Task<ReportFetchResult> FetchAsync(ReportCard card, HttpMessageHandler handler, BasicCredential? credential = null, CancellationToken cancellation = default)
    {
        Validate(new ReportCardSettings { Cards = [card] });
        (string? body, string? error) = await GetAsync(ReportUri(card), card.SourceUrl, handler, credential, cancellation).ConfigureAwait(false);
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
    public static async Task<(IReadOnlyList<ReportChoice> Reports, string? Error)> ListReportsAsync(string sourceUrl, HttpMessageHandler handler, BasicCredential? credential = null, CancellationToken cancellation = default)
    {
        QuickWebApps.ValidateUrl(sourceUrl, "Report source");
        (string? body, string? error) = await GetAsync(WorkspaceUri(sourceUrl), sourceUrl, handler, credential, cancellation).ConfigureAwait(false);
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

    private static async Task<(string? Body, string? Error)> GetAsync(Uri uri, string sourceUrl, HttpMessageHandler handler, BasicCredential? credential, CancellationToken cancellation)
    {
        var source = new Uri(EnsureSlash(sourceUrl));
        string where = source.GetLeftPart(UriPartial.Authority);
        if (credential is not null && ReportAuth.RefusalToSend(source) is string refusal) return (null, refusal);
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout, MaxResponseContentBufferSize = MaxResponseBytes };
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        // The header is attached to this single request for the card's own origin; redirects are never followed.
        if (credential is not null && ReportAuth.Origin(uri) == ReportAuth.Origin(source)) request.Headers.Authorization = ReportAuth.Header(credential);
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            string? body = await ReadBoundedAsync(response.Content, cancellation).ConfigureAwait(false);
            if (body is null) return (null, $"The response from {where} is larger than {MaxResponseBytes / 1024 / 1024} MB; open it in Mongoku instead.");
            if (response.IsSuccessStatusCode) return (body, null);
            bool basicChallenge = response.Headers.WwwAuthenticate.Any(x => x.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase));
            return (null, response.StatusCode switch
            {
                HttpStatusCode.Unauthorized when basicChallenge && credential is null =>
                    $"Mongoku at {where} asks for a user name and password. Save them under Sign-in in Manage report cards (kept in Windows Credential Manager).",
                HttpStatusCode.Unauthorized when basicChallenge =>
                    $"Mongoku at {where} rejected the saved user name or password. Update them under Sign-in in Manage report cards.",
                HttpStatusCode.Unauthorized =>
                    $"Mongoku at {where} uses web sign-in (OIDC). Report cards cannot sign in that way; open the report in Mongoku.",
                HttpStatusCode.Forbidden =>
                    $"Mongoku at {where} refused access (403) for this account.",
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
