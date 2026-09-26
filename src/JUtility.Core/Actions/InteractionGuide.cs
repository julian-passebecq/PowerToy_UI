namespace JUtility.Core.Actions;

// V2.1 Interaction settings + MX Master / Logi Options+ guide. Power Ops never talks to Logitech software:
// Options+ sends ordinary keyboard shortcuts, which Power Ops receives through RegisterHotKey like any keyboard.

/// <param name="Candidates">Preference order. Ctrl+Alt+Shift+letter combinations are typeable (Options+ records a pressed
/// keystroke), almost never used by applications, and pass <see cref="HotkeyGesture.GlobalConflict"/>.</param>
public sealed record RecommendedShortcut(string ActionId, string Purpose, IReadOnlyList<string> Candidates);

public enum ShortcutPlanOutcome
{
    AlreadyBound,
    Added,
    /// <summary>The existing binding is owned by another program in Windows, so it could never fire; a free one replaced it.</summary>
    Replaced,
    NoFreeCandidate,
    LimitReached,
}

public sealed record ShortcutPlanLine(string ActionId, string Label, ShortcutPlanOutcome Outcome, string? Gesture, string Message);

public sealed record GuideStep(string Title, string Detail, string? CopyText = null, bool IsWarning = false);

public static class InteractionGuide
{
    public static string Describe(InteractionMode mode) => mode switch
    {
        InteractionMode.Off => "No overlay at startup. Actions stay available from the Actions menu and any global shortcuts you enabled.",
        InteractionMode.QuickShelf => "Show the compact Quick Shelf when Power Ops starts.",
        InteractionMode.QuickRing => "No startup overlay; a global shortcut opens the Quick Ring at the pointer.",
        InteractionMode.MxMasterGuide => "Map MX Master buttons in Logi Options+ to Power Ops shortcuts. Works the same with any keyboard.",
        InteractionMode.Hybrid => "Recommended: a mouse button or shortcut opens the Quick Ring; another shortcut shows or hides full Power Ops.",
        _ => string.Empty,
    };

    private static readonly RecommendedShortcut Ring = new(QuickActionCatalog.RingShow, "Open the Quick Ring at the pointer", ["Ctrl+Alt+Shift+R", "Ctrl+Alt+Shift+Q", "Ctrl+Alt+Shift+F8"]);
    private static readonly RecommendedShortcut Toggle = new(QuickActionCatalog.AppToggle, "Show or hide full Power Ops", ["Ctrl+Alt+Shift+P", "Ctrl+Alt+Shift+Space", "Ctrl+Alt+Shift+F9"]);
    private static readonly RecommendedShortcut Shelf = new(QuickActionCatalog.ShelfToggle, "Show or hide the Quick Shelf", ["Ctrl+Alt+Shift+L", "Ctrl+Alt+Shift+F7"]);
    private static readonly RecommendedShortcut Capture = new(QuickActionCatalog.CaptureQuick, "Quick Capture", ["Ctrl+Alt+Shift+N", "Ctrl+Alt+Shift+F10"]);
    private static readonly RecommendedShortcut Clipboard = new(QuickActionCatalog.ClipboardOpen, "Clipboard library", ["Ctrl+Alt+Shift+V", "Ctrl+Alt+Shift+F11"]);

    public static IReadOnlyList<RecommendedShortcut> Recommended(InteractionMode mode) => mode switch
    {
        InteractionMode.QuickShelf => [Shelf, Toggle],
        InteractionMode.QuickRing => [Ring, Toggle],
        InteractionMode.MxMasterGuide or InteractionMode.Hybrid => [Ring, Toggle, Capture, Clipboard],
        _ => [],
    };

    /// <summary>
    /// Adds the mode's recommended shortcuts to <paramref name="settings"/>. An action that already has a working binding
    /// keeps it; a binding Windows reports as owned by another program (<paramref name="isFree"/> false) could never fire,
    /// so it is replaced by a free suggestion when one exists. Candidates must be unused in the settings and free in
    /// Windows. Enables global shortcuts when something was added. Idempotent.
    /// </summary>
    public static IReadOnlyList<ShortcutPlanLine> AddRecommended(QuickActionSettings settings, InteractionMode mode, Func<HotkeyGesture, bool> isFree)
    {
        var lines = new List<ShortcutPlanLine>();
        foreach (RecommendedShortcut recommended in Recommended(mode))
        {
            string label = QuickActionCatalog.Get(recommended.ActionId).Label;
            ShortcutBinding? existing = settings.GlobalShortcuts.FirstOrDefault(x => x.ActionId == recommended.ActionId);
            if (existing is not null && isFree(HotkeyGesture.Parse(existing.Gesture)))
            {
                lines.Add(new(recommended.ActionId, label, ShortcutPlanOutcome.AlreadyBound, existing.Gesture, $"{label}: keeps {existing.Gesture}."));
                continue;
            }

            if (existing is null && settings.GlobalShortcuts.Count >= QuickActionLayouts.MaxGlobalShortcuts)
            {
                lines.Add(new(recommended.ActionId, label, ShortcutPlanOutcome.LimitReached, null, $"{label}: not added, the shortcut list is full ({QuickActionLayouts.MaxGlobalShortcuts})."));
                continue;
            }

            var used = settings.GlobalShortcuts.Select(x => HotkeyGesture.Parse(x.Gesture).ToString()).ToHashSet(StringComparer.Ordinal);
            HotkeyGesture? chosen = recommended.Candidates
                .Select(HotkeyGesture.Parse)
                .FirstOrDefault(g => !used.Contains(g.ToString()) && HotkeyGesture.GlobalConflict(g) is null && isFree(g));
            if (chosen is null)
            {
                lines.Add(new(recommended.ActionId, label, ShortcutPlanOutcome.NoFreeCandidate, existing?.Gesture,
                    (existing is null ? string.Empty : $"{existing.Gesture} is used by another program and ")
                    + $"{label}: every suggested shortcut ({string.Join(", ", recommended.Candidates)}) is taken. Choose one in Global shortcuts."));
                continue;
            }

            if (existing is not null)
            {
                existing.Gesture = chosen.ToString();
                lines.Add(new(recommended.ActionId, label, ShortcutPlanOutcome.Replaced, chosen.ToString(),
                    $"{label}: its shortcut is already used by another program, so it now uses {chosen}."));
            }
            else
            {
                settings.GlobalShortcuts.Add(new ShortcutBinding { Gesture = chosen.ToString(), ActionId = recommended.ActionId });
                lines.Add(new(recommended.ActionId, label, ShortcutPlanOutcome.Added, chosen.ToString(), $"{label}: added {chosen}."));
            }

            settings.GlobalShortcutsEnabled = true;
        }

        return lines.AsReadOnly();
    }

    /// <summary>Logi Options+ steps built from the shortcuts actually configured, so the guide can never promise a missing binding.</summary>
    public static IReadOnlyList<GuideStep> MxMasterSteps(QuickActionSettings settings, string? mouseSummonWarning)
    {
        string? Gesture(string actionId) => settings.GlobalShortcutsEnabled
            ? settings.GlobalShortcuts.FirstOrDefault(x => x.ActionId == actionId)?.Gesture
            : null;
        GuideStep Map(string button, string actionId, string optional = "")
        {
            string label = QuickActionCatalog.Get(actionId).Label;
            string? gesture = Gesture(actionId);
            return gesture is null
                ? new GuideStep($"{button} → {label}", $"No global shortcut is bound to {label} yet. Use \"Add recommended shortcuts\" or Global shortcuts first.{optional}", null, true)
                : new GuideStep($"{button} → {label}", $"In Logi Options+ choose {button}, action \"Keyboard shortcut\", and press {gesture}.{optional}", gesture);
        }

        var steps = new List<GuideStep>
        {
            new("Before you start", "Install Logi Options+ from Logitech and pair the mouse. Power Ops does not need a Logitech driver: Options+ sends a keyboard shortcut and Power Ops reacts to it like any keyboard. Keep Power Ops running."),
        };
        if (mouseSummonWarning is not null) steps.Add(new GuideStep("Avoid double reaction", mouseSummonWarning, null, true));
        steps.Add(Map("Sense Panel or gesture button (click)", QuickActionCatalog.RingShow,
            " On an MX Master 4, the thumb Sense Panel (Options+ calls it \"Show Actions Ring\") is the easiest place: choose Gestures > Custom, then set Click."
            + " If Options+ does not record the shortcut because the Quick Ring opens instead, untick Actions > Global shortcuts, record it, then tick it again."));
        // Moves are Windows' own commands: they keep working when Power Ops is closed. Quick Capture and the clipboard are in the ring.
        steps.Add(new GuideStep("Hold + move up → all windows (Windows)", "Optional. Same button, Hold + move up: Keyboard shortcut Win+Tab (Task view).", "Win+Tab"));
        steps.Add(new GuideStep("Hold + move down → desktop (Windows)", "Optional. Hold + move down: Keyboard shortcut Win+D (show or hide the desktop).", "Win+D"));
        steps.Add(new GuideStep("Hold + move left / right → half screen (Windows)", "Optional. Hold + move left: Win+Left, Hold + move right: Win+Right (snap the window to one half).", "Win+Left"));
        steps.Add(Map("Any spare button", QuickActionCatalog.AppToggle, " Optional."));
        steps.Add(Map("Any spare button", QuickActionCatalog.CaptureQuick, " Optional: it is also in the Quick Ring."));
        steps.Add(Map("Any spare button", QuickActionCatalog.ClipboardOpen, " Optional: it is also in the Quick Ring."));
        steps.Add(new GuideStep("Esc → put Power Ops away", "Esc hides Power Ops (or minimizes it, depending on the window mode). A button mapped to Esc in Options+, for example the middle button, therefore closes Power Ops too."));
        const string builtIn = "Nothing to set up: while Power Ops is in front, the mouse's own Back and Forward buttons switch Power Ops tabs; "
            + "everywhere else they keep their normal browser behaviour. From the keyboard:";
        steps.Add(new GuideStep("Back → previous Power Ops tab", builtIn + " Ctrl+Shift+Tab.", "Ctrl+Shift+Tab"));
        steps.Add(new GuideStep("Forward → next Power Ops tab", builtIn + " Ctrl+Tab.", "Ctrl+Tab"));
        steps.Add(new GuideStep(
            "Test",
            "Press each mouse button once. If nothing happens, open Actions > Global shortcuts: a shortcut that another program already owns is listed there as not registered."));
        return steps.AsReadOnly();
    }
}
