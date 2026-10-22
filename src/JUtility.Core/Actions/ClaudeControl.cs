using System.Globalization;
using System.Net;
using System.Text.Json;

namespace JUtility.Core.Actions;

// Optional link to the user's local Claude Control server (claude-control repo, server/server.py).
// Off unless quick-actions.json has a "ClaudeControl" block: the address and start command are user settings,
// never defaults in this public repo. Power Ops only sends GET /api/health and GET /api/status to a loopback
// address; every write happens inside the embedded page, which carries its own token.

public sealed class ClaudeControlSettings
{
    /// <summary>Page shown in the "Claude Control" tab, e.g. the server's home.html. Must be a loopback http(s) address.</summary>
    public string Url { get; set; } = "";
    /// <summary>Optional absolute path of the script that starts the server (.cmd, .bat or .exe).</summary>
    public string? StartCommand { get; set; }
}

public sealed record ClaudeControlPlan(double FiveHour, double Week, string Sampled);

public sealed record ClaudeControlSessions(int Open, int Running, int NeedsYou);

public sealed record ClaudeControlUrgent(string Time, string Level, string Source, string Project, string Text, Uri? Link);

public sealed record ClaudeControlStatus(
    string Generated,
    ClaudeControlPlan? Plan,
    ClaudeControlSessions? Sessions,
    int? Todo,
    int? OpenPrs,
    int? ChecksToAct,
    IReadOnlyList<ClaudeControlUrgent> Urgent,
    string AuditName,
    string AuditStatus);

public static class ClaudeControl
{
    public const string Name = "Claude Control";
    public const string StartActionPrefix = "control:";
    public const string StartActionId = StartActionPrefix + "start";
    public const int MaxResponseBytes = 256 * 1024, MaxUrgent = 20;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>Fixed identity of the synthesized "Claude Control" web app, so its tab survives restarts.</summary>
    public static readonly Guid WebAppId = new("c1a0de00-c0de-4c0e-9000-000000007430");

    public static bool IsEnabled(QuickActionSettings? settings) => settings?.ClaudeControl is not null;

    /// <summary>The embedded tab, expressed as an ordinary web app so it reuses the Web module; null when not configured.</summary>
    public static WebAppEntry? WebApp(QuickActionSettings? settings) => settings?.ClaudeControl is { } control
        ? new WebAppEntry { Id = WebAppId, Name = Name, Url = control.Url.Trim(), OpenMode = WebOpenMode.Embedded }
        : null;

    public static void Validate(ClaudeControlSettings? control, IReadOnlyList<WebAppEntry>? webApps)
    {
        if (control is null) return;
        QuickWebApps.ValidateUrl(control.Url, Name);
        var uri = new Uri(control.Url.Trim());
        if (!uri.IsLoopback) throw new InvalidDataException($"{Name}: the address must be on this computer (127.0.0.1 or localhost).");
        if (webApps?.Any(x => x?.Id == WebAppId) == true) throw new InvalidDataException($"{Name}: a web app re-uses the reserved {Name} identifier.");
        if (control.StartCommand is { } command)
        {
            string trimmed = command.Trim();
            if (trimmed.Length is 0 or > 260 || !Path.IsPathFullyQualified(trimmed))
                throw new InvalidDataException($"{Name}: the start command must be a full path such as C:\\tools\\start-control.cmd.");
            if (Path.GetExtension(trimmed).ToLowerInvariant() is not (".cmd" or ".bat" or ".exe"))
                throw new InvalidDataException($"{Name}: the start command must be a .cmd, .bat or .exe file.");
        }
    }

    public static Uri HealthUri(ClaudeControlSettings control) => new(new Uri(control.Url.Trim()), "/api/health");

    public static Uri StatusUri(ClaudeControlSettings control) => new(new Uri(control.Url.Trim()), "/api/status");

    /// <summary>True when GET /api/health answers 2xx with {"ok": true} within <see cref="Timeout"/>.</summary>
    public static async Task<bool> IsHealthyAsync(ClaudeControlSettings control, HttpMessageHandler handler, CancellationToken cancellation = default)
    {
        (string? body, _) = await GetAsync(HealthUri(control), handler, cancellation).ConfigureAwait(false);
        if (body is null) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>GET /api/status; (null, reason) when the server is off, slow or answers something unexpected.</summary>
    public static async Task<(ClaudeControlStatus? Status, string? Error)> FetchStatusAsync(ClaudeControlSettings control, HttpMessageHandler handler, CancellationToken cancellation = default)
    {
        (string? body, string? error) = await GetAsync(StatusUri(control), handler, cancellation).ConfigureAwait(false);
        if (body is null) return (null, error);
        try { return (ParseStatus(body), null); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return (null, $"{Name} answered an unexpected status format: {ex.Message}"); }
    }

    public static ClaudeControlStatus ParseStatus(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("status is not a JSON object.");

        ClaudeControlPlan? plan = Object(root, "plan") is { } p
            ? new ClaudeControlPlan(Percent(p, "five_hour"), Percent(p, "week"), Text(p, "sampled"))
            : null;
        ClaudeControlSessions? sessions = Object(root, "sessions") is { } s
            ? new ClaudeControlSessions(Count(s, "open") ?? 0, Count(s, "running") ?? 0, Count(s, "needs_you") ?? 0)
            : null;

        var urgent = new List<ClaudeControlUrgent>();
        if (root.TryGetProperty("urgent", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string text = Text(item, "text");
                if (text.Length == 0) continue;
                urgent.Add(new ClaudeControlUrgent(Text(item, "time"), Text(item, "level"), Text(item, "source"), Text(item, "project"), text, SafeLink(Text(item, "link"))));
                if (urgent.Count == MaxUrgent) break;
            }
        }

        JsonElement? audit = Object(root, "latest_audit");
        return new ClaudeControlStatus(
            Text(root, "generated"), plan, sessions, Count(root, "todo"), Count(root, "open_prs"), Count(root, "checks_to_act"), urgent,
            audit is { } a ? Text(a, "name") : "", audit is { } b ? Text(b, "status") : "");
    }

    /// <summary>Only claude:// deep links and http(s) pages are opened from the card; anything else is dropped.</summary>
    public static Uri? SafeLink(string? link) =>
        Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) && uri.Scheme is "claude" or "https" or "http" && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;

    private static async Task<(string? Body, string? Error)> GetAsync(Uri uri, HttpMessageHandler handler, CancellationToken cancellation)
    {
        if (!uri.IsLoopback) return (null, $"{Name} must run on this computer.");
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout, MaxResponseContentBufferSize = MaxResponseBytes };
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        string where = uri.GetLeftPart(UriPartial.Authority);
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return (null, $"{Name} at {where} answered HTTP {(int)response.StatusCode}.");
            return (await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false), null);
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return (null, $"{Name} at {where} did not answer within {Timeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException)
        {
            return (null, $"{Name} is not running at {where}.");
        }
    }

    private static JsonElement? Object(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? (value.GetString() ?? "").Trim() : "";

    private static int? Count(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int n) ? Math.Max(0, n) : null;

    private static double Percent(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double v) ? Math.Clamp(v, 0, 100) : double.NaN;

    /// <summary>"2026-09-25T18:25" -> "18:25" for today's stamps, otherwise "2026-09-24 18:25".</summary>
    public static string Friendly(string stamp, DateTime now)
    {
        if (!DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime at)) return stamp;
        return at.Date == now.Date ? at.ToString("HH:mm", CultureInfo.InvariantCulture) : at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
