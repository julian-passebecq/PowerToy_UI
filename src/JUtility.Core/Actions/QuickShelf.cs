namespace JUtility.Core.Actions;

/// <summary>One button on a Quick Shelf or Quick Ring.</summary>
public sealed record QuickSurfaceItem(string Id, string Label, string Glyph, string ToolTip, string? UnavailableReason)
{
    public bool IsAvailable => UnavailableReason is null;

    /// <summary>The Segoe Fluent/MDL2 character for <see cref="Glyph"/>.</summary>
    public string GlyphText => char.ConvertFromUtf32(Convert.ToInt32(Glyph, 16));
}

public static class QuickSurfaceModel
{
    /// <summary>
    /// Buttons for the given ids. Availability is probed once per item, only when a surface is shown or
    /// hovered (never on a timer), and the reason is surfaced instead of hiding the action.
    /// </summary>
    public static IReadOnlyList<QuickSurfaceItem> Build(
        QuickActionSettings settings,
        IEnumerable<string> ids,
        Func<string, string?> unavailableReason,
        Func<string, string?> globalGesture,
        Func<string, QuickActionDefinition?>? resolve = null)
    {
        return ids.Select(id =>
        {
            // resolve: runtime-only actions such as Tool Launcher entries, which quick-actions.json cannot describe.
            QuickActionDefinition action = resolve?.Invoke(id) ?? QuickActionLayouts.Describe(settings, id);
            string? reason = unavailableReason(id);
            string? gesture = globalGesture(id);
            string tip = action.Label
                + (string.IsNullOrEmpty(gesture) ? string.Empty : $" ({gesture})")
                + "\n" + action.Description
                + (reason is null ? string.Empty : "\nUnavailable: " + reason);
            return new QuickSurfaceItem(id, action.Label, action.Glyph, tip, reason);
        }).ToList().AsReadOnly();
    }
}

public static class QuickShelfModel
{
    /// <summary>The Shelf is shown automatically only in Quick Shelf mode; shelf.toggle can still show it on request.</summary>
    public static bool ShowAtStartup(QuickActionSettings settings) => settings.Mode == InteractionMode.QuickShelf;

    public static IReadOnlyList<QuickSurfaceItem> Build(
        QuickActionSettings settings,
        Guid workspaceId,
        Func<string, string?> unavailableReason,
        Func<string, string?> globalGesture,
        Func<string, QuickActionDefinition?>? resolve = null) =>
        QuickSurfaceModel.Build(settings, QuickActionLayouts.ResolveShelf(settings, workspaceId), unavailableReason, globalGesture, resolve);
}

public static class QuickRingModel
{
    public static IReadOnlyList<QuickSurfaceItem> Build(
        QuickActionSettings settings,
        Guid workspaceId,
        Func<string, string?> unavailableReason,
        Func<string, string?> globalGesture,
        Func<string, QuickActionDefinition?>? resolve = null) =>
        QuickSurfaceModel.Build(settings, QuickActionLayouts.ResolveRing(settings, workspaceId), unavailableReason, globalGesture, resolve);

    /// <summary>The second-level ring of a "group:" slot; null when the group no longer exists.</summary>
    public static IReadOnlyList<QuickSurfaceItem>? BuildGroup(
        QuickActionSettings settings,
        string groupActionId,
        Func<string, string?> unavailableReason,
        Func<string, string?> globalGesture,
        Func<string, QuickActionDefinition?>? resolve = null) =>
        QuickRingGroups.Find(settings, groupActionId) is { } group
            ? QuickSurfaceModel.Build(settings, group.Items, unavailableReason, globalGesture, resolve)
            : null;

    /// <summary>Slot centre relative to the ring centre: slot 0 at the top, then clockwise (screen Y grows downward).</summary>
    public static (double X, double Y) SlotOffset(int index, int count, double radius)
    {
        if (count < 1 || index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        double angle = -Math.PI / 2 + 2 * Math.PI * index / count;
        return (Math.Round(radius * Math.Cos(angle), 6), Math.Round(radius * Math.Sin(angle), 6));
    }

    /// <summary>Arrow-key movement around the ring with wrap-around; -1 (nothing focused) enters at the first or last slot.</summary>
    public static int Move(int current, int count, int delta)
    {
        if (count < 1) return -1;
        if (current < 0 || current >= count) return delta >= 0 ? 0 : count - 1;
        return ((current + delta) % count + count) % count;
    }

    /// <summary>Digit keys 1-8 pick a slot directly; null when that slot does not exist.</summary>
    public static int? SlotForDigit(int digit, int count) => digit >= 1 && digit <= count ? digit - 1 : null;
}
