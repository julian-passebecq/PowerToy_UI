using System.Text.Json;
using System.Text.Json.Nodes;
using JUtility.Core.Credentials;
using JUtility.Core.Models;

namespace JUtility.Core.Capture;

/// <summary>
/// "+ -> type/paste -> Save". Builds ordinary Capture entries (business schema v7 StickyNoteEntry); no second note engine.
/// </summary>
public static class QuickCaptureRules
{
    public const int MaxTitle = 120;

    /// <summary>A lone http(s) URL suggests Link; anything else suggests Note. The user can always change it.</summary>
    public static CaptureKind Suggest(string? text) => SingleUrl(text) is not null ? CaptureKind.Bookmark : CaptureKind.QuickNote;

    public static string? SingleUrl(string? text)
    {
        string trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace)) return null;
        return Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
    }

    public static string? FirstUrl(string? text)
    {
        foreach (string word in (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (Uri.TryCreate(word.Trim('<', '>', '(', ')', '"', '\''), UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https") return uri.AbsoluteUri;
        return null;
    }

    public static StickyNoteEntry Create(CaptureKind kind, string? text, Guid? projectId, string? labels, DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind) || kind == CaptureKind.Transcript) throw new InvalidDataException("Choose Note, Link, To-do, Read later or Inbox.");
        if (projectId == Guid.Empty) throw new InvalidDataException("Invalid project.");
        string body = (text ?? "").Replace("\r\n", "\n").Trim();
        if (body.Length == 0) throw new InvalidDataException("Type or paste something to capture.");

        string url = kind is CaptureKind.Bookmark or CaptureKind.ReadLater ? FirstUrl(body) ?? "" : "";
        string remainder = url.Length > 0 && SingleUrl(body) is not null ? "" : body;
        string title = url.Length > 0 && remainder.Length == 0 ? UrlTitle(new Uri(url)) : FirstLine(body);
        if (title.Length > MaxTitle) title = title[..(MaxTitle - 1)].TrimEnd() + "…";
        // Keep the full text unless it is exactly the title (a short single-line note).
        string noteText = remainder == title ? "" : remainder;

        return new StickyNoteEntry
        {
            Kind = kind,
            Title = title,
            Text = noteText,
            Url = url,
            Labels = (labels ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim(),
            Status = kind == CaptureKind.Todo ? "Open" : "",
            ProjectId = projectId,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
    }

    private static string FirstLine(string text)
    {
        int newline = text.IndexOf('\n');
        return (newline < 0 ? text : text[..newline]).Trim();
    }

    private static string UrlTitle(Uri uri)
    {
        string path = uri.AbsolutePath.TrimEnd('/');
        return uri.Host + (path.Length > 0 ? path : "");
    }
}

/// <summary>
/// powerops.atlasnote-handoff/1: a reviewed, one-way, non-secret export of selected captures for AtlasNote's own importer.
/// Carries stable source IDs and revisions; no credentials, .env data or media bytes. Power Ops keeps the originals.
/// </summary>
public static class AtlasNoteHandoff
{
    public const string Format = "powerops.atlasnote-handoff", SourceApp = "powerops";
    public const int Version = 1, MaxItems = 1000;

    public sealed record Warning(Guid NoteId, string Title, string Reason);

    /// <summary>Items whose text looks like it holds a secret; the UI leaves them unselected by default.</summary>
    public static IReadOnlyList<Warning> Review(IEnumerable<StickyNoteEntry> notes) => notes
        .Select(n => (Note: n, Reason: SecretHeuristics.Reason(n.Title) ?? SecretHeuristics.Reason(n.Text) ?? SecretHeuristics.Reason(n.Url) ?? SecretHeuristics.Reason(n.Labels)))
        .Where(x => x.Reason is not null)
        .Select(x => new Warning(x.Note.Id, x.Note.Title, x.Reason!))
        .ToList();

    public static string Create(IReadOnlyList<StickyNoteEntry> notes, IReadOnlyDictionary<Guid, string> projectNames, DateTimeOffset generatedUtc)
    {
        if (notes.Count == 0) throw new InvalidDataException("Select at least one capture to hand off.");
        if (notes.Count > MaxItems) throw new InvalidDataException($"Hand off at most {MaxItems} captures at a time.");
        string observedAt = generatedUtc.ToUniversalTime().ToString("O");
        var items = new JsonArray();
        foreach (var note in notes)
        {
            items.Add(new JsonObject
            {
                ["projectRef"] = note.ProjectId?.ToString("D"),
                ["projectName"] = note.ProjectId is Guid id && projectNames.TryGetValue(id, out string? name) ? name : null,
                ["sourceApp"] = SourceApp,
                ["sourceObjectId"] = note.Id.ToString("D"),
                ["sourceRevision"] = note.UpdatedUtc.ToUniversalTime().ToString("O"),
                ["observedAt"] = observedAt,
                ["authority"] = SourceApp,
                ["visibility"] = "private",
                ["freshness"] = "snapshot",
                ["kind"] = KindName(note.Kind),
                ["title"] = note.Title,
                ["text"] = note.Text,
                ["url"] = note.Url.Length == 0 ? null : note.Url,
                ["subject"] = note.Subject.Length == 0 ? null : note.Subject,
                ["labels"] = new JsonArray(note.Labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["status"] = note.Status.Length == 0 ? null : note.Status,
                ["priority"] = note.Priority.Length == 0 ? null : note.Priority,
                ["dueUtc"] = note.DueUtc?.ToUniversalTime().ToString("O"),
                ["isCompleted"] = note.IsCompleted,
                ["isArchived"] = note.IsArchived,
                ["createdUtc"] = note.CreatedUtc.ToUniversalTime().ToString("O"),
                ["updatedUtc"] = note.UpdatedUtc.ToUniversalTime().ToString("O"),
            });
        }
        var root = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["sourceApp"] = SourceApp,
            ["generatedAt"] = observedAt,
            ["freshness"] = "snapshot",
            ["containsCredentialValues"] = false,
            ["containsMediaBinaries"] = false,
            ["importContract"] = "One-way reviewed handoff. AtlasNote owns the importer and should de-duplicate on sourceApp + sourceObjectId + sourceRevision. Power Ops keeps these captures until AtlasNote explicitly confirms an import.",
            ["items"] = items,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string KindName(CaptureKind kind) => kind switch
    {
        CaptureKind.Todo => "todo",
        CaptureKind.QuickNote => "note",
        CaptureKind.Bookmark => "link",
        CaptureKind.ReadLater => "readLater",
        CaptureKind.Transcript => "transcript",
        _ => "inbox",
    };
}
