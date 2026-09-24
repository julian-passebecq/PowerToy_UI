namespace JUtility.Core.Actions;

public sealed record QuickShelfItem(string Id, string Label, string Glyph, string ToolTip, string? UnavailableReason)
{
    public bool IsAvailable => UnavailableReason is null;

    /// <summary>The Segoe Fluent/MDL2 character for <see cref="Glyph"/>.</summary>
    public string GlyphText => char.ConvertFromUtf32(Convert.ToInt32(Glyph, 16));
}

public static class QuickShelfModel
{
    /// <summary>The Shelf is shown automatically only in Quick Shelf mode; shelf.toggle can still show it on request.</summary>
    public static bool ShowAtStartup(QuickActionSettings settings) => settings.Mode == InteractionMode.QuickShelf;

    /// <summary>
    /// Buttons for the active workspace. Availability is probed once per item, only when the Shelf is
    /// shown or hovered (never on a timer), and the reason is surfaced instead of hiding the action.
    /// </summary>
    public static IReadOnlyList<QuickShelfItem> Build(
        QuickActionSettings settings,
        Guid workspaceId,
        Func<string, string?> unavailableReason,
        Func<string, string?> globalGesture)
    {
        return QuickActionLayouts.ResolveShelf(settings, workspaceId).Select(id =>
        {
            QuickActionDefinition action = QuickActionLayouts.Describe(settings, id);
            string? reason = unavailableReason(id);
            string? gesture = globalGesture(id);
            string tip = action.Label
                + (string.IsNullOrEmpty(gesture) ? string.Empty : $" ({gesture})")
                + "\n" + action.Description
                + (reason is null ? string.Empty : "\nUnavailable: " + reason);
            return new QuickShelfItem(id, action.Label, action.Glyph, tip, reason);
        }).ToList().AsReadOnly();
    }
}
