namespace JUtility.Core.Actions;

// V2.1 Quick Actions: ONE typed catalog feeds every front end (full UI, Quick Shelf,
// Quick Ring, keyboard shortcuts and optional MX Master mappings via Logi Options+).
// Surfaces never implement behaviour themselves; they invoke an action ID through the dispatcher.

public enum ActionRisk
{
    Safe,
    Confirm,
    Destructive,
}

public enum ActionSurface
{
    FullUi,
    InAppShortcut,
    GlobalShortcut,
    QuickShelf,
    QuickRing,
}

/// <param name="Glyph">Segoe MDL2/Fluent glyph code point (hex), rendered by the WPF surfaces.</param>
/// <param name="InAppShortcut">Documentation metadata for an existing focused-window shortcut; never registered globally.</param>
/// <param name="GlobalAllowed">May run while another application is focused (global hotkey, Ring, Shelf, MX mapping).</param>
public sealed record QuickActionDefinition(
    string Id,
    string Label,
    string Category,
    string Description,
    string Glyph,
    string? InAppShortcut,
    bool GlobalAllowed,
    ActionRisk Risk);

public static class QuickActionCatalog
{
    public const string AppToggle = "app.toggle", AppOpen = "app.open", RingShow = "ring.show", ShelfToggle = "shelf.toggle";
    public const string CaptureRegion = "capture.region", CaptureQuick = "capture.quick", ClipboardOpen = "clipboard.open";
    public const string CaptureScreen = "capture.screen";
    public const string FolderDownloads = "folder.downloads", FolderExplorer = "folder.explorer", TerminalOpen = "terminal.open";
    public const string FolderDesktop = "folder.desktop";
    public const string WorkspaceResume = "workspace.resume", WorkspaceNext = "workspace.next", WorkspacePrevious = "workspace.previous";
    public const string TabNext = "tab.next", TabPrevious = "tab.previous";
    // File tray (V2.3). Item actions act on the tray row they were started from; from a global shortcut, the
    // Quick Shelf or the Quick Ring they act on the newest file.
    public const string TrayShow = "tray.show", TrayCopyLatest = "tray.copyLatest";
    public const string TrayCopyFile = "tray.copyFile", TrayCopyText = "tray.copyText", TrayCopyImage = "tray.copyImage";
    public const string TrayOpen = "tray.open", TrayReveal = "tray.reveal", TrayMove = "tray.move", TrayRemove = "tray.remove";

    // IDs are persisted in quick-actions.json and referenced by external mappings: never rename, only add.
    // Deliberately absent from this slice: mail.latestCode and other provider-backed actions (later opt-in adapters).
    public static readonly IReadOnlyList<QuickActionDefinition> All = Array.AsReadOnly(new[]
    {
        new QuickActionDefinition(AppToggle, "Show / hide Power Ops", "Power Ops", "Toggle the main window without changing workspace state.", "E8A7", null, true, ActionRisk.Safe),
        new QuickActionDefinition(AppOpen, "Open Power Ops", "Power Ops", "Bring the full Power Ops window to the front.", "E80F", null, true, ActionRisk.Safe),
        new QuickActionDefinition(RingShow, "Quick Ring", "Power Ops", "Show the radial Quick Ring near the pointer.", "E8A9", null, true, ActionRisk.Safe),
        new QuickActionDefinition(ShelfToggle, "Quick Shelf", "Power Ops", "Show or hide the compact Quick Shelf bar.", "E8A0", null, true, ActionRisk.Safe),
        new QuickActionDefinition(CaptureRegion, "Screenshot (region)", "Capture", "Start the supported Windows region-capture flow.", "E7A8", null, true, ActionRisk.Safe),
        new QuickActionDefinition(CaptureScreen, "Screenshot (whole screen)", "Capture", "Copy the whole screen under the pointer to the clipboard, ready for Ctrl+V.", "E7F4", null, true, ActionRisk.Safe),
        new QuickActionDefinition(CaptureQuick, "Quick Capture", "Capture", "Add a note, task or link to the local Capture inbox.", "E70B", null, true, ActionRisk.Safe),
        new QuickActionDefinition(ClipboardOpen, "Clipboard library", "Capture", "Open the reusable text, screenshot and clip library.", "E77F", null, true, ActionRisk.Safe),
        new QuickActionDefinition(FolderDownloads, "Downloads", "Files", "Open the Windows Downloads folder in Explorer.", "E896", null, true, ActionRisk.Safe),
        new QuickActionDefinition(FolderDesktop, "Desktop folder", "Files", "Open your Windows Desktop folder in Explorer.", "E8FC", null, true, ActionRisk.Safe),
        new QuickActionDefinition(FolderExplorer, "Explorer folder", "Files", "Open the configured Explorer folder.", "E8B7", "Ctrl+Shift+E", true, ActionRisk.Safe),
        new QuickActionDefinition(TerminalOpen, "Terminal", "Tools", "Open the configured terminal. Never runs imported commands.", "E756", null, true, ActionRisk.Safe),
        new QuickActionDefinition(WorkspaceResume, "Resume workspace", "Workspace", "Reopen Power Ops on the active workspace and tab.", "E768", null, true, ActionRisk.Safe),
        new QuickActionDefinition(WorkspaceNext, "Next workspace", "Workspace", "Switch to the next saved workspace view.", "E893", null, true, ActionRisk.Safe),
        new QuickActionDefinition(WorkspacePrevious, "Previous workspace", "Workspace", "Switch to the previous saved workspace view.", "E892", null, true, ActionRisk.Safe),
        new QuickActionDefinition(TabNext, "Next tab", "Workspace", "Activate the next tab when Power Ops is focused.", "E72A", "Ctrl+Tab", false, ActionRisk.Safe),
        new QuickActionDefinition(TabPrevious, "Previous tab", "Workspace", "Activate the previous tab when Power Ops is focused.", "E72B", "Ctrl+Shift+Tab", false, ActionRisk.Safe),
        new QuickActionDefinition(TrayShow, "File tray", "File tray", "Show recently received files (Downloads and chosen folders) to drag or paste into a chat.", "E7B8", null, true, ActionRisk.Safe),
        new QuickActionDefinition(TrayCopyLatest, "Copy last received file", "File tray", "Put the newest tray file on the clipboard as a file, ready for Ctrl+V.", "E7C3", null, true, ActionRisk.Safe),
        new QuickActionDefinition(TrayCopyFile, "Copy as file", "File tray", "Copy the tray file as a file (the newest one outside the tray), ready for Ctrl+V.", "E8C8", null, true, ActionRisk.Safe),
        new QuickActionDefinition(TrayCopyText, "Copy text", "File tray", "Copy the file's text: PDF text, Word or text files, or offline Windows OCR for images and scanned PDFs.", "E8D2", null, true, ActionRisk.Safe),
        new QuickActionDefinition(TrayCopyImage, "Copy as image", "File tray", "Copy an image, or page 1 of a PDF, as a picture for chats that do not take PDFs.", "EB9F", null, true, ActionRisk.Safe),
        new QuickActionDefinition(TrayOpen, "Open file", "File tray", "Open the tray file with its default app.", "E8E5", null, true, ActionRisk.Safe),
        new QuickActionDefinition(TrayReveal, "Show in folder", "File tray", "Show the tray file in Explorer.", "E838", null, true, ActionRisk.Safe),
        new QuickActionDefinition(TrayMove, "Move to project folder", "File tray", "Move the tray file into a Repository Hub project's folder (you pick the project and confirm).", "E8DE", null, true, ActionRisk.Safe),
        new QuickActionDefinition(TrayRemove, "Remove from tray", "File tray", "Hide the file from the tray for this session. The file itself is never deleted.", "E711", null, true, ActionRisk.Safe),
    });

    public static QuickActionDefinition? Find(string? id) => All.FirstOrDefault(x => x.Id == id);

    public static QuickActionDefinition Get(string id) => Find(id)
        ?? throw new InvalidDataException($"Unknown quick action: {id}");

    /// <summary>Surfaces that run while another application may be focused.</summary>
    public static bool IsOutOfApp(ActionSurface surface) =>
        surface is ActionSurface.GlobalShortcut or ActionSurface.QuickRing or ActionSurface.QuickShelf;
}

public enum QuickActionOutcome
{
    Executed,
    NeedsConfirmation,
    Unavailable,
    NotAllowed,
    Failed,
}

public sealed record QuickActionResult(QuickActionOutcome Outcome, string ActionId, string Message)
{
    public bool Succeeded => Outcome == QuickActionOutcome.Executed;
}

/// <summary>
/// The single execution implementation of one action. <paramref name="unavailableReason"/> is only
/// evaluated on demand (when a surface is shown or the action is invoked) — never on a timer.
/// </summary>
public sealed class QuickActionHandler(Action execute, Func<string?>? unavailableReason = null)
{
    internal Action Execute { get; } = execute ?? throw new ArgumentNullException(nameof(execute));
    internal Func<string?>? UnavailableReason { get; } = unavailableReason;
}

public sealed class QuickActionDispatcher
{
    private readonly Dictionary<string, QuickActionHandler> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QuickActionDefinition> _dynamic = new(StringComparer.Ordinal);

    /// <summary>Built-in catalog followed by the currently registered user-defined actions (web apps).</summary>
    public IEnumerable<QuickActionDefinition> Definitions => QuickActionCatalog.All.Concat(_dynamic.Values);

    public QuickActionDefinition? Definition(string? actionId) =>
        QuickActionCatalog.Find(actionId) ?? (actionId is not null && _dynamic.TryGetValue(actionId, out var definition) ? definition : null);

    /// <summary>
    /// Replaces every user-defined action under <paramref name="prefix"/> (e.g. "web:"). User actions can never
    /// shadow a built-in ID, and each still has exactly one implementation.
    /// </summary>
    public void ReplaceDynamic(string prefix, IEnumerable<(QuickActionDefinition Definition, QuickActionHandler Handler)> actions)
    {
        if (string.IsNullOrEmpty(prefix) || !prefix.EndsWith(':')) throw new InvalidOperationException("Dynamic action prefixes end with ':'.");
        var incoming = actions.ToList();
        foreach (var (definition, handler) in incoming)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (!definition.Id.StartsWith(prefix, StringComparison.Ordinal) || QuickActionCatalog.Find(definition.Id) is not null)
                throw new InvalidOperationException($"'{definition.Id}' is not a valid {prefix} action ID.");
        }

        if (incoming.Select(x => x.Definition.Id).Distinct(StringComparer.Ordinal).Count() != incoming.Count)
            throw new InvalidOperationException("Duplicate user-defined action.");

        foreach (string id in _dynamic.Keys.Where(x => x.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _dynamic.Remove(id);
            _handlers.Remove(id);
        }

        foreach (var (definition, handler) in incoming)
        {
            _dynamic[definition.Id] = definition;
            _handlers[definition.Id] = handler;
        }
    }

    public void Register(string actionId, QuickActionHandler handler)
    {
        QuickActionCatalog.Get(actionId);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd(actionId, handler))
        {
            throw new InvalidOperationException($"Quick action '{actionId}' already has an implementation. Surfaces must share it.");
        }
    }

    public bool IsRegistered(string actionId) => _handlers.ContainsKey(actionId);

    /// <summary>Null when the action can run now; otherwise a user-facing reason. Probes the handler on demand only.</summary>
    public string? UnavailableReason(string actionId)
    {
        if (Definition(actionId) is null) return "Unknown action.";
        if (!_handlers.TryGetValue(actionId, out var handler)) return "Not available in this build.";
        try { return handler.UnavailableReason?.Invoke(); }
        catch (Exception ex) { return "Availability check failed: " + ex.Message; }
    }

    public QuickActionResult Invoke(string actionId, ActionSurface surface, bool confirmed = false)
    {
        var definition = Definition(actionId);
        if (definition is null) return new(QuickActionOutcome.NotAllowed, actionId, "Unknown action.");
        if (QuickActionCatalog.IsOutOfApp(surface) && !definition.GlobalAllowed)
            return new(QuickActionOutcome.NotAllowed, actionId, $"{definition.Label} only works while Power Ops is focused.");
        if (definition.Risk == ActionRisk.Destructive && surface != ActionSurface.FullUi)
            return new(QuickActionOutcome.NotAllowed, actionId, $"{definition.Label} can only be started from the full Power Ops window.");
        string? reason = UnavailableReason(actionId);
        if (reason is not null) return new(QuickActionOutcome.Unavailable, actionId, reason);
        if (definition.Risk != ActionRisk.Safe && !confirmed)
            return new(QuickActionOutcome.NeedsConfirmation, actionId, $"Confirm: {definition.Label}.");
        try
        {
            _handlers[actionId].Execute();
            return new(QuickActionOutcome.Executed, actionId, definition.Label);
        }
        catch (Exception ex)
        {
            // A failing action must never take down the Ring/Shelf/hotkey pump; the surface shows the message.
            return new(QuickActionOutcome.Failed, actionId, $"{definition.Label} failed: {ex.Message}");
        }
    }
}
