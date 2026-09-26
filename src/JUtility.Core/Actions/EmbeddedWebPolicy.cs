using JUtility.Core.Workspaces;

namespace JUtility.Core.Actions;

// Rules for the optional embedded "Web" module (WebView2). Kept in Core so they are testable without a browser.
public static class EmbeddedWebPolicy
{
    public const string ModuleId = "web";

    /// <summary>One persistent WebView2 profile per Power Ops data directory (Mongoku keeps its tabs in localStorage).</summary>
    public static string UserDataFolder(string dataDirectory) => Path.Combine(Path.GetFullPath(dataDirectory), "webview2");

    /// <summary>Top-level navigation inside an embedded view: http(s) only (keeps OIDC redirects working, blocks file: etc.).</summary>
    public static bool AllowNavigation(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) && parsed.Scheme is "http" or "https";

    /// <summary>
    /// Pop-ups / target=_blank never open a second embedded browser: http(s) goes to the user's default browser,
    /// anything else is dropped. Null = drop.
    /// </summary>
    public static Uri? ExternalTarget(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) && parsed.Scheme is "http" or "https" ? parsed : null;

    /// <summary>Web tab action id stored in a session tab (in <see cref="SessionTab.Filter"/>), or null.</summary>
    public static string? TabWebApp(SessionTab tab) =>
        tab.ModuleId == ModuleId && QuickWebApps.IsWebActionId(tab.Filter) ? tab.Filter : null;

    /// <summary>
    /// Embedded web apps that may keep a live view: those referenced by the current workspace's open tabs and still
    /// configured as Embedded. Everything else is disposed, so switching workspace or closing tabs frees the browser.
    /// </summary>
    public static IReadOnlySet<string> LiveViews(WorkspaceProfile workspace, QuickActionSettings? settings) =>
        workspace.Tabs
            .Select(TabWebApp)
            .Where(id => id is not null && QuickWebApps.Find(settings, id)?.OpenMode == WebOpenMode.Embedded)
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Shows the web app in the current workspace: re-uses its tab if one exists, otherwise opens a new "Web" tab
    /// (or retargets the active tab when the tab limit is reached). Returns the tab that shows it.
    /// </summary>
    public static SessionTab ShowInWorkspace(WorkspaceProfile workspace, string webActionId)
    {
        if (!QuickWebApps.IsWebActionId(webActionId)) throw new InvalidDataException("Not a web app action.");
        SessionTab? existing = workspace.Tabs.FirstOrDefault(x => TabWebApp(x) == webActionId);
        if (existing is not null)
        {
            workspace.ActiveTabId = existing.Id;
            return existing;
        }

        if (!workspace.VisibleModules.Contains(ModuleId)) workspace.VisibleModules.Add(ModuleId);
        SessionTab tab = workspace.Tabs.Count < WorkspaceSessions.MaxTabs
            ? WorkspaceSessions.AddTab(workspace, ModuleId)
            : WorkspaceSessions.ActiveTab(workspace);
        tab.ModuleId = ModuleId;
        tab.Search = string.Empty;
        tab.Filter = webActionId;
        tab.Families.Clear();
        tab.Providers.Clear();
        tab.Subjects.Clear();
        workspace.ActiveTabId = tab.Id;
        return tab;
    }
}
