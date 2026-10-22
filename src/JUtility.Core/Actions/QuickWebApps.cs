namespace JUtility.Core.Actions;

// V2.1 web destinations. Browser/AppWindow launch an external browser; Embedded shows the app in a Power Ops
// "Web" tab through WebView2, created lazily on first use (EmbeddedWebPolicy, docs/v2/WEB_SURFACES.md).

public enum WebOpenMode
{
    /// <summary>The user's default browser, normal tab.</summary>
    Browser,
    /// <summary>Chrome/Edge <c>--app=</c> window: no tabs or address bar, but the user's existing browser profile and sign-ins.</summary>
    AppWindow,
    /// <summary>A Power Ops "Web" tab (WebView2, separate profile under the data directory). Meant for local/self-hosted tools such as Mongoku.</summary>
    Embedded,
}

public enum WebBrowserChoice
{
    Auto,
    Chrome,
    Edge,
}

public sealed class WebAppEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public WebOpenMode OpenMode { get; set; } = WebOpenMode.AppWindow;
    public WebBrowserChoice Browser { get; set; } = WebBrowserChoice.Auto;
}

public sealed record WebAppPreset(string Name, string Url, WebOpenMode OpenMode, string Note);

public static class QuickWebApps
{
    public const string Prefix = "web:";
    public const int MaxWebApps = 24, MaxName = 60, MaxUrl = 2048;
    public const string Glyph = "E774"; // Globe

    // Starting points only; nothing is added until the user chooses one. Localhost ports are the projects' defaults.
    public static readonly IReadOnlyList<WebAppPreset> Presets = Array.AsReadOnly(new[]
    {
        new WebAppPreset("Mongoku", "http://localhost:3100/", WebOpenMode.Embedded, "Mongoku-datapass default port (pnpm dev / mongoku CLI). Opens in a Power Ops Web tab; switch to App window if its sign-in uses Google."),
        new WebAppPreset("Grafana", "http://localhost:3000/", WebOpenMode.AppWindow, "Grafana default port; change to your Grafana Cloud or server URL."),
        new WebAppPreset("Gemini", "https://gemini.google.com/app", WebOpenMode.AppWindow, "Uses your existing browser sign-in."),
        new WebAppPreset("ChatGPT", "https://chatgpt.com/", WebOpenMode.AppWindow, "Uses your existing browser sign-in."),
        new WebAppPreset("Claude", "https://claude.ai/new", WebOpenMode.AppWindow, "Uses your existing browser sign-in."),
        // claude.ai pages need the user's claude.ai sign-in, so the default is an app window on the existing browser profile
        // (Embedded would need a separate sign-in inside WebView2). The private board link is personal: paste it, never commit it.
        new WebAppPreset("Effort Board", "https://claude.ai/code/artifacts", WebOpenMode.AppWindow,
            "Paste your Effort Board link (https://claude.ai/artifact/...) into URL, then Save. Opens with your browser's claude.ai sign-in. Add it to the Shelf with Actions > Customize Quick Shelf."),
    });

    public static bool IsWebActionId(string? id) => id?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static string ActionId(Guid id) => Prefix + id.ToString("N");

    public static QuickActionDefinition Definition(WebAppEntry app)
    {
        string where = Uri.TryCreate(app.Url, UriKind.Absolute, out Uri? uri) ? uri.Authority : app.Url;
        string how = app.OpenMode switch
        {
            WebOpenMode.AppWindow => "in its own app window",
            WebOpenMode.Embedded => "in a Power Ops tab",
            _ => "in your default browser",
        };
        return new QuickActionDefinition(ActionId(app.Id), app.Name.Trim(), "Web apps", $"Open {where} {how}.", Glyph, null, true, ActionRisk.Safe);
    }

    public static IReadOnlyList<QuickActionDefinition> Definitions(QuickActionSettings? settings) =>
        All(settings).Select(Definition).ToList().AsReadOnly();

    /// <summary>The user's web apps followed by the Claude Control tab when it is configured.</summary>
    public static IReadOnlyList<WebAppEntry> All(QuickActionSettings? settings) =>
        (settings?.WebApps ?? []).Where(x => x is not null)
            .Concat(ClaudeControl.WebApp(settings) is { } control ? [control] : [])
            .ToList().AsReadOnly();

    public static void Validate(List<WebAppEntry>? apps)
    {
        if (apps is null || apps.Count > MaxWebApps) throw new InvalidDataException($"Keep at most {MaxWebApps} web apps.");
        var ids = new HashSet<Guid>();
        foreach (WebAppEntry app in apps)
        {
            if (app is null || app.Id == Guid.Empty || !ids.Add(app.Id)) throw new InvalidDataException("Missing or duplicate web app identifier.");
            if (string.IsNullOrWhiteSpace(app.Name) || app.Name.Trim().Length > MaxName) throw new InvalidDataException($"Web app names need 1-{MaxName} characters.");
            if (!Enum.IsDefined(app.OpenMode) || !Enum.IsDefined(app.Browser)) throw new InvalidDataException($"{app.Name}: invalid open mode or browser.");
            ValidateUrl(app.Url, app.Name);
        }
    }

    /// <summary>Only plain http(s) destinations; never file:, javascript: or credentials embedded in the URL.</summary>
    public static void ValidateUrl(string? url, string name = "Web app")
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrl || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri))
            throw new InvalidDataException($"{name}: enter a full address such as http://localhost:3100/.");
        if (uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException($"{name}: only http:// and https:// addresses can be opened.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException($"{name}: do not put user names or passwords in the address; sign in on the page instead.");
    }

    public static WebAppEntry? Find(QuickActionSettings? settings, string? actionId) =>
        All(settings).FirstOrDefault(x => ActionId(x.Id) == actionId);

    public static WebAppEntry FromPreset(WebAppPreset preset) => new() { Name = preset.Name, Url = preset.Url, OpenMode = preset.OpenMode };

    /// <summary>
    /// Removes a deleted web app from every layout, override and shortcut so the settings stay valid.
    /// A layout that would become empty falls back to the built-in default.
    /// </summary>
    public static void RemoveWebApp(QuickActionSettings settings, Guid webAppId)
    {
        string actionId = ActionId(webAppId);
        settings.WebApps.RemoveAll(x => x.Id == webAppId);
        foreach (RingGroup group in settings.RingGroups) group.Items.Remove(actionId);
        foreach (RingGroup empty in settings.RingGroups.Where(x => x.Items.Count == 0).ToList()) QuickRingGroups.Remove(settings, empty.Id);
        settings.Ring.Remove(actionId);
        if (settings.Ring.Count == 0) settings.Ring = [.. QuickActionLayouts.DefaultRing];
        settings.Shelf.Remove(actionId);
        if (settings.Shelf.Count == 0) settings.Shelf = [.. QuickActionLayouts.DefaultShelf];
        foreach (WorkspaceActionOverride entry in settings.WorkspaceOverrides)
        {
            entry.Ring?.Remove(actionId);
            if (entry.Ring?.Count == 0) entry.Ring = null;
            entry.Shelf?.Remove(actionId);
            if (entry.Shelf?.Count == 0) entry.Shelf = null;
        }

        settings.WorkspaceOverrides.RemoveAll(x => x.Ring is null && x.Shelf is null);
        settings.GlobalShortcuts.RemoveAll(x => x.ActionId == actionId);
    }

    /// <summary>
    /// Which installed Chromium browser opens app windows. Auto follows the default browser when it is Chrome
    /// (keeping its signed-in profile), otherwise prefers Edge. Null = neither is installed.
    /// </summary>
    public static string? ChooseAppBrowser(WebBrowserChoice choice, string? defaultBrowserProgId, string? chromePath, string? edgePath)
    {
        string? chrome = string.IsNullOrWhiteSpace(chromePath) ? null : chromePath;
        string? edge = string.IsNullOrWhiteSpace(edgePath) ? null : edgePath;
        return choice switch
        {
            WebBrowserChoice.Chrome => chrome,
            WebBrowserChoice.Edge => edge,
            _ when defaultBrowserProgId?.StartsWith("ChromeHTML", StringComparison.OrdinalIgnoreCase) == true && chrome is not null => chrome,
            _ => edge ?? chrome,
        };
    }
}
