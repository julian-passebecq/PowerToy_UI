namespace JUtility.Core.Actions;

// V2.4 Quick Ring sub-rings: a "group:" slot opens a second ring (Folders, Apps...) in place of the first one.
// Groups live in quick-actions.json next to the Ring; like web apps, each one is a dynamic action usable on every
// surface (from a shortcut or the Shelf, it opens the Quick Ring directly on that group). Groups never nest.
public sealed class RingGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Segoe Fluent/MDL2 code point (hex), as in the built-in catalog.</summary>
    public string Glyph { get; set; } = "E8B7";
    public List<string> Items { get; set; } = [];
}

public static class QuickRingGroups
{
    public const string Prefix = "group:";
    public const int MaxGroups = 8, MaxNameLength = 40;

    // Fixed IDs so the suggested layout is stable across fresh installs and tests.
    public static readonly Guid FoldersId = new("5c1f3a0e-6a1b-4e0e-9a4f-2f5b8d1c0a01");
    public static readonly Guid AppsId = new("5c1f3a0e-6a1b-4e0e-9a4f-2f5b8d1c0a02");

    public static string ActionId(Guid id) => Prefix + id.ToString("N");

    public static bool IsGroupActionId(string? id) => id is not null && id.StartsWith(Prefix, StringComparison.Ordinal);

    public static RingGroup? Find(QuickActionSettings? settings, string? actionId) =>
        settings?.RingGroups?.FirstOrDefault(x => ActionId(x.Id) == actionId);

    public static QuickActionDefinition Definition(RingGroup group) => new(
        ActionId(group.Id),
        group.Name + " ›",
        "Ring group",
        $"Open the {group.Name} ring ({group.Items.Count} actions). Esc or the centre goes back.",
        group.Glyph,
        null,
        true,
        ActionRisk.Safe);

    public static IReadOnlyList<QuickActionDefinition> Definitions(QuickActionSettings? settings) =>
        (settings?.RingGroups ?? []).Select(Definition).ToList().AsReadOnly();

    /// <summary>
    /// The suggested two-level layout: capture first, then Folders › and Apps › sub-rings. Apps holds the terminal and
    /// every web app already configured (up to the ring limit); tools can be added from Customize Quick Ring.
    /// </summary>
    public static void ApplySuggested(QuickActionSettings settings)
    {
        var apps = new List<string> { QuickActionCatalog.TerminalOpen };
        apps.AddRange(QuickWebApps.All(settings).Select(x => QuickWebApps.ActionId(x.Id)).Take(QuickActionLayouts.MaxRing - 1));
        settings.RingGroups =
        [
            new RingGroup
            {
                Id = FoldersId, Name = "Folders", Glyph = "E8B7",
                Items = [QuickActionCatalog.FolderDownloads, QuickActionCatalog.FolderDesktop, QuickActionCatalog.FolderExplorer, QuickActionCatalog.TrayShow],
            },
            new RingGroup { Id = AppsId, Name = "Apps", Glyph = "ECAA", Items = apps },
            .. settings.RingGroups.Where(x => x.Id != FoldersId && x.Id != AppsId),
        ];
        settings.Ring =
        [
            QuickActionCatalog.CaptureRegion, QuickActionCatalog.CaptureScreen, QuickActionCatalog.CaptureQuick,
            QuickActionCatalog.ClipboardOpen, ActionId(FoldersId), ActionId(AppsId), QuickActionCatalog.WorkspaceResume,
        ];
    }

    /// <summary>Deletes a group and every reference to it; a layout that would become empty falls back to the built-in default.</summary>
    public static void Remove(QuickActionSettings settings, Guid groupId)
    {
        string actionId = ActionId(groupId);
        settings.RingGroups.RemoveAll(x => x.Id == groupId);
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

    internal static void Validate(QuickActionSettings settings, IReadOnlyList<QuickActionDefinition> dynamic)
    {
        if (settings.RingGroups is null || settings.RingGroups.Count > MaxGroups)
            throw new InvalidDataException($"Invalid Quick Ring group collection (at most {MaxGroups}).");
        var seen = new HashSet<Guid>();
        foreach (RingGroup group in settings.RingGroups)
        {
            if (group is null || group.Id == Guid.Empty || !seen.Add(group.Id))
                throw new InvalidDataException("Missing or duplicate Quick Ring group.");
            if (string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > MaxNameLength || group.Name.Any(char.IsControl))
                throw new InvalidDataException($"A Quick Ring group needs a name of 1 to {MaxNameLength} characters.");
            if (!IsGlyph(group.Glyph)) throw new InvalidDataException($"The {group.Name} group has an invalid icon.");
            if (group.Items?.Any(IsGroupActionId) == true)
                throw new InvalidDataException($"The {group.Name} group cannot contain another group.");
            try { QuickActionLayouts.ValidateLayout(group.Items, ActionSurface.QuickRing, dynamic); }
            catch (InvalidDataException ex) { throw new InvalidDataException($"{group.Name} group: {ex.Message}"); }
        }
    }

    private static bool IsGlyph(string? glyph) =>
        glyph is { Length: 4 or 5 } && glyph.All(Uri.IsHexDigit) && Convert.ToInt32(glyph, 16) is >= 0xE000 and <= 0xF8FF;
}

// Tool Launcher entries as "tool:" actions (VS Code, a terminal, any program). Tools live in the business workspace, not
// in quick-actions.json, so layouts only check the ID shape; a removed tool shows as unavailable instead of breaking the file.
public static class QuickToolActions
{
    public const string Prefix = "tool:";

    public static string ActionId(Guid toolId) => Prefix + toolId.ToString("N");

    public static bool IsToolActionId(string? id) => id is not null && id.StartsWith(Prefix, StringComparison.Ordinal);

    public static Guid? ToolId(string? id) =>
        IsToolActionId(id) && Guid.TryParseExact(id![Prefix.Length..], "N", out Guid toolId) ? toolId : null;

    public static QuickActionDefinition Definition(Guid toolId, string name, string command) => new(
        ActionId(toolId),
        string.IsNullOrWhiteSpace(name) ? "Tool" : name.Trim(),
        "Tools",
        $"Start {(string.IsNullOrWhiteSpace(name) ? "this tool" : name.Trim())} from Tool Launcher.",
        GlyphFor(name, command),
        null,
        true,
        ActionRisk.Safe);

    /// <summary>Placeholder while the Tool Launcher list is not known (validation) or the tool was removed.</summary>
    public static QuickActionDefinition Missing(string id) =>
        new(id, "Tool (not found)", "Tools", "This Tool Launcher entry no longer exists.", "ECAA", null, true, ActionRisk.Safe);

    public static string GlyphFor(string? name, string? command)
    {
        string text = ((name ?? "") + " " + (command ?? "")).ToLowerInvariant();
        if (text.Contains("code") || text.Contains("studio") || text.Contains("cursor")) return "E943";
        if (text.Contains("terminal") || text.Contains("wt.exe") || text.Contains("pwsh") || text.Contains("powershell") || text.Contains("cmd")) return "E756";
        if (text.Contains("chrome") || text.Contains("edge") || text.Contains("firefox")) return "E774";
        return "ECAA";
    }
}
