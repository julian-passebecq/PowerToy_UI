using System.Text.Json;
using System.Text.Json.Serialization;
using JUtility.Core.Models;

namespace JUtility.Core.Actions;

public enum InteractionMode
{
    Off,
    QuickShelf,
    QuickRing,
    MxMasterGuide,
    Hybrid,
}

public enum ShelfOrientation
{
    Horizontal,
    Vertical,
}

public sealed class ShortcutBinding
{
    public string Gesture { get; set; } = "";
    public string ActionId { get; set; } = "";
}

/// <summary>Null list = inherit the default layout. Views only: never an access-control boundary.</summary>
public sealed class WorkspaceActionOverride
{
    public Guid WorkspaceId { get; set; }
    public List<string>? Ring { get; set; }
    public List<string>? Shelf { get; set; }
}

// Separate bounded file (quick-actions.json) so shell schema 1 and business schema v7 stay unchanged.
public sealed class QuickActionSettings
{
    [JsonRequired]
    public string Format { get; set; } = QuickActionLayouts.FormatName;
    [JsonRequired]
    public int SchemaVersion { get; set; } = 1;
    // Off by default: no Ring/Shelf window, no global hotkey registration until the user opts in.
    public InteractionMode Mode { get; set; } = InteractionMode.Off;
    public bool GlobalShortcutsEnabled { get; set; }
    public List<ShortcutBinding> GlobalShortcuts { get; set; } =
        [new ShortcutBinding { Gesture = "Ctrl+Alt+Space", ActionId = QuickActionCatalog.AppToggle }];
    public List<string> Ring { get; set; } = [.. QuickActionLayouts.DefaultRing];
    public List<string> Shelf { get; set; } = [.. QuickActionLayouts.DefaultShelf];
    public ShelfOrientation ShelfOrientation { get; set; } = ShelfOrientation.Horizontal;
    public bool ShelfAlwaysOnTop { get; set; } = true;
    public bool ShelfAutoHide { get; set; }
    // Last dragged position in WPF device-independent pixels; null = default placement. Clamped on screen when shown.
    public double? ShelfLeft { get; set; }
    public double? ShelfTop { get; set; }
    public List<WorkspaceActionOverride> WorkspaceOverrides { get; set; } = [];
    // Opt-in user destinations (Mongoku, Grafana, Gemini...). Each becomes a "web:" action usable on every surface.
    public List<WebAppEntry> WebApps { get; set; } = [];
    // Optional local Claude Control server (tab, start action, health dot, Launchpad status). Null = off.
    public ClaudeControlSettings? ClaudeControl { get; set; }
    // V2.4 sub-rings ("group:" slots). Missing in older files = none; the Ring keeps working unchanged.
    public List<RingGroup> RingGroups { get; set; } = [];
}

public static class QuickActionLayouts
{
    public const string FormatName = "powerops-quick-actions";
    public const int MaxRing = 8, MaxShelf = 10, MaxOverrides = 20, MaxGlobalShortcuts = 16;

    // Ring centre always opens full Power Ops, so app.open is not a slot.
    public static readonly IReadOnlyList<string> DefaultRing = Array.AsReadOnly(new[]
    {
        QuickActionCatalog.CaptureRegion, QuickActionCatalog.FolderDownloads, QuickActionCatalog.CaptureQuick,
        QuickActionCatalog.ClipboardOpen, QuickActionCatalog.FolderExplorer, QuickActionCatalog.TerminalOpen,
        QuickActionCatalog.WorkspaceResume,
    });

    public static readonly IReadOnlyList<string> DefaultShelf = Array.AsReadOnly(new[]
    {
        QuickActionCatalog.AppOpen, QuickActionCatalog.CaptureRegion, QuickActionCatalog.CaptureQuick,
        QuickActionCatalog.ClipboardOpen, QuickActionCatalog.FolderDownloads, QuickActionCatalog.FolderExplorer,
        QuickActionCatalog.TerminalOpen, QuickActionCatalog.WorkspaceResume, QuickActionCatalog.TrayShow,
    });

    /// <summary>Fresh installs start with the suggested two-level ring (Folders › and Apps › sub-rings).</summary>
    public static QuickActionSettings Defaults()
    {
        var settings = new QuickActionSettings();
        QuickRingGroups.ApplySuggested(settings);
        return settings;
    }

    /// <summary>Web apps and ring groups: the user-defined actions a layout may reference.</summary>
    public static IReadOnlyList<QuickActionDefinition> Dynamic(QuickActionSettings? settings) =>
        QuickWebApps.Definitions(settings).Concat(QuickRingGroups.Definitions(settings)).ToList().AsReadOnly();

    /// <summary>Catalog actions that may be placed on the given surface (same rules as <see cref="ValidateLayout"/>).</summary>
    public static IReadOnlyList<QuickActionDefinition> Eligible(ActionSurface surface, QuickActionSettings? settings = null)
    {
        string self = surface == ActionSurface.QuickRing ? QuickActionCatalog.RingShow : QuickActionCatalog.ShelfToggle;
        return QuickActionCatalog.All.Concat(Dynamic(settings))
            .Where(x => x.GlobalAllowed && x.Risk != ActionRisk.Destructive && x.Id != self).ToList().AsReadOnly();
    }

    /// <summary>Built-in catalog entry or one of this settings file's web apps.</summary>
    public static QuickActionDefinition Describe(QuickActionSettings settings, string id) => Lookup(id, Dynamic(settings));

    private static QuickActionDefinition Lookup(string id, IReadOnlyList<QuickActionDefinition>? dynamic) =>
        QuickActionCatalog.Find(id)
        ?? dynamic?.FirstOrDefault(x => x.Id == id)
        ?? (QuickToolActions.ToolId(id) is not null ? QuickToolActions.Missing(id) : null)
        ?? throw new InvalidDataException(QuickWebApps.IsWebActionId(id)
            ? "A layout or shortcut refers to a web app that no longer exists."
            : QuickRingGroups.IsGroupActionId(id)
                ? "A layout or shortcut refers to a Quick Ring group that no longer exists."
                : $"Unknown quick action: {id}");

    public static IReadOnlyList<string> ResolveRing(QuickActionSettings settings, Guid workspaceId) =>
        (Override(settings, workspaceId)?.Ring ?? settings.Ring).AsReadOnly();

    public static IReadOnlyList<string> ResolveShelf(QuickActionSettings settings, Guid workspaceId) =>
        (Override(settings, workspaceId)?.Shelf ?? settings.Shelf).AsReadOnly();

    /// <summary>Pass null to inherit the default ring for this workspace.</summary>
    public static void SetWorkspaceRing(QuickActionSettings settings, Guid workspaceId, IEnumerable<string>? ids) =>
        SetOverride(settings, workspaceId, ids, ActionSurface.QuickRing);

    public static void SetWorkspaceShelf(QuickActionSettings settings, Guid workspaceId, IEnumerable<string>? ids) =>
        SetOverride(settings, workspaceId, ids, ActionSurface.QuickShelf);

    public static void Validate(QuickActionSettings settings)
    {
        if (settings is null || settings.Format != FormatName || settings.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported quick-actions format/version. Existing files were not rewritten.");
        if (!Enum.IsDefined(settings.Mode) || !Enum.IsDefined(settings.ShelfOrientation))
            throw new InvalidDataException("Invalid interaction mode or shelf orientation.");
        foreach (double? coordinate in new[] { settings.ShelfLeft, settings.ShelfTop })
        {
            if (coordinate is double value && (!double.IsFinite(value) || Math.Abs(value) > 100_000))
                throw new InvalidDataException("Invalid Quick Shelf position.");
        }
        QuickWebApps.Validate(settings.WebApps);
        JUtility.Core.Actions.ClaudeControl.Validate(settings.ClaudeControl, settings.WebApps);
        var webApps = Dynamic(settings);
        QuickRingGroups.Validate(settings, webApps);
        ValidateLayout(settings.Ring, ActionSurface.QuickRing, webApps);
        ValidateLayout(settings.Shelf, ActionSurface.QuickShelf, webApps);
        if (settings.WorkspaceOverrides is null || settings.WorkspaceOverrides.Count > MaxOverrides)
            throw new InvalidDataException("Invalid workspace action override collection.");
        var seen = new HashSet<Guid>();
        foreach (var entry in settings.WorkspaceOverrides)
        {
            if (entry is null || entry.WorkspaceId == Guid.Empty || !seen.Add(entry.WorkspaceId))
                throw new InvalidDataException("Missing or duplicate workspace action override.");
            if (entry.Ring is not null) ValidateLayout(entry.Ring, ActionSurface.QuickRing, webApps);
            if (entry.Shelf is not null) ValidateLayout(entry.Shelf, ActionSurface.QuickShelf, webApps);
        }
        ValidateGlobalShortcuts(settings.GlobalShortcuts, webApps);
    }

    public static void ValidateLayout(List<string>? ids, ActionSurface surface, IReadOnlyList<QuickActionDefinition>? webApps = null)
    {
        int max = surface == ActionSurface.QuickRing ? MaxRing : MaxShelf;
        string name = surface == ActionSurface.QuickRing ? "Quick Ring" : "Quick Shelf";
        if (ids is null || ids.Count < 1 || ids.Count > max)
            throw new InvalidDataException($"{name} needs between 1 and {max} actions.");
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidDataException($"{name} contains a duplicate action.");
        string self = surface == ActionSurface.QuickRing ? QuickActionCatalog.RingShow : QuickActionCatalog.ShelfToggle;
        foreach (string id in ids)
        {
            var action = Lookup(id, webApps);
            if (!action.GlobalAllowed) throw new InvalidDataException($"{action.Label} only works inside Power Ops and cannot be placed on the {name}.");
            if (action.Risk == ActionRisk.Destructive) throw new InvalidDataException($"Destructive actions cannot be placed on the {name}.");
            if (id == self) throw new InvalidDataException($"The {name} cannot contain itself.");
        }
    }

    public static void ValidateGlobalShortcuts(List<ShortcutBinding>? bindings, IReadOnlyList<QuickActionDefinition>? webApps = null)
    {
        if (bindings is null || bindings.Count > MaxGlobalShortcuts)
            throw new InvalidDataException("Invalid global shortcut collection.");
        var gestures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding is null) throw new InvalidDataException("Invalid global shortcut.");
            var gesture = HotkeyGesture.Parse(binding.Gesture);
            string? conflict = HotkeyGesture.GlobalConflict(gesture);
            if (conflict is not null) throw new InvalidDataException(conflict);
            if (!gestures.Add(gesture.ToString())) throw new InvalidDataException($"{gesture} is bound more than once.");
            var action = Lookup(binding.ActionId, webApps);
            if (!action.GlobalAllowed || action.Risk == ActionRisk.Destructive)
                throw new InvalidDataException($"{action.Label} cannot be bound to a global shortcut.");
        }
    }

    /// <summary>
    /// Warns when Power Ops' own low-level mouse hook (Summon window mode) would intercept the same
    /// physical button that Logi Options+ is expected to remap. Null when there is no overlap.
    /// </summary>
    public static string? MouseDoubleInterceptionWarning(WindowBehaviorMode behavior, SummonMouseBinding binding, InteractionMode mode)
    {
        if (behavior != WindowBehaviorMode.Summon || mode is not (InteractionMode.MxMasterGuide or InteractionMode.Hybrid)) return null;
        if (binding is not (SummonMouseBinding.MouseButton4 or SummonMouseBinding.MouseButton5)) return null;
        string button = binding == SummonMouseBinding.MouseButton4 ? "Back (button 4)" : "Forward (button 5)";
        return $"Power Ops' mouse summon hook already uses {button}. If Logi Options+ also remaps that button, both will react. " +
               "Either assign it only in Options+ (send a Power Ops shortcut) or switch Summon to a different binding.";
    }

    private static WorkspaceActionOverride? Override(QuickActionSettings settings, Guid workspaceId) =>
        settings.WorkspaceOverrides.FirstOrDefault(x => x.WorkspaceId == workspaceId);

    private static void SetOverride(QuickActionSettings settings, Guid workspaceId, IEnumerable<string>? ids, ActionSurface surface)
    {
        if (workspaceId == Guid.Empty) throw new InvalidDataException("Missing workspace.");
        List<string>? list = ids?.ToList();
        if (list is not null) ValidateLayout(list, surface, Dynamic(settings));
        var entry = Override(settings, workspaceId);
        if (entry is null)
        {
            if (list is null) return;
            if (settings.WorkspaceOverrides.Count >= MaxOverrides) throw new InvalidOperationException("Workspace override limit reached.");
            entry = new WorkspaceActionOverride { WorkspaceId = workspaceId };
            settings.WorkspaceOverrides.Add(entry);
        }
        if (surface == ActionSurface.QuickRing) entry.Ring = list; else entry.Shelf = list;
        if (entry.Ring is null && entry.Shelf is null) settings.WorkspaceOverrides.Remove(entry);
    }
}

// Values match the Win32 RegisterHotKey MOD_* flags so the WPF layer can pass them through unchanged.
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key, int VirtualKey)
{
    public const int ModNoRepeat = 0x4000;

    /// <summary>fsModifiers argument for RegisterHotKey (auto-repeat suppressed).</summary>
    public int NativeModifiers => (int)Modifiers | ModNoRepeat;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    public static HotkeyGesture Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64) throw new InvalidDataException("Enter a shortcut such as Ctrl+Alt+Space.");
        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (string raw in text.Split('+'))
        {
            string token = raw.Trim();
            HotkeyModifiers modifier = token.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None,
            };
            if (modifier != HotkeyModifiers.None)
            {
                if (modifiers.HasFlag(modifier)) throw new InvalidDataException($"'{token}' is repeated in {text}.");
                modifiers |= modifier;
                continue;
            }
            if (key is not null) throw new InvalidDataException("A shortcut can only contain one non-modifier key.");
            key = token;
        }
        if (key is null) throw new InvalidDataException("The shortcut needs a key after the modifiers.");
        (string canonical, int vk) = KeyFor(key) ?? throw new InvalidDataException($"'{key}' is not supported for global shortcuts.");
        if (!modifiers.HasFlag(HotkeyModifiers.Control) && !modifiers.HasFlag(HotkeyModifiers.Alt) && !modifiers.HasFlag(HotkeyModifiers.Windows))
            throw new InvalidDataException("Global shortcuts need Ctrl, Alt or Win so normal typing is never captured.");
        return new HotkeyGesture(modifiers, canonical, vk);
    }

    /// <summary>
    /// Policy check only: a null result does not prove Windows will accept the registration.
    /// RegisterHotKey can still fail at runtime and that failure must be shown to the user.
    /// </summary>
    public static string? GlobalConflict(HotkeyGesture gesture)
    {
        var m = gesture.Modifiers;
        bool letterOrDigit = gesture.Key.Length == 1;
        if (m == HotkeyModifiers.Control && letterOrDigit)
            return $"{gesture} is a common application shortcut (and Ctrl+1..9 switches Power Ops tabs). Add Alt or Shift.";
        if (m == HotkeyModifiers.Alt && (letterOrDigit || gesture.Key is "Space" or "F4"))
            return $"{gesture} is used by Windows menus and window commands.";
        if (m == HotkeyModifiers.Windows || m == (HotkeyModifiers.Windows | HotkeyModifiers.Shift))
            return $"{gesture} is reserved by Windows.";
        if (m == (HotkeyModifiers.Control | HotkeyModifiers.Shift) && gesture.Key is "E" or "T")
            return $"{gesture} is already a Power Ops in-app shortcut.";
        return null;
    }

    private static (string, int)? KeyFor(string key)
    {
        string upper = key.ToUpperInvariant();
        if (upper.Length == 1 && upper[0] is >= 'A' and <= 'Z') return (upper, upper[0]);
        if (upper.Length == 1 && upper[0] is >= '0' and <= '9') return (upper, upper[0]);
        if (upper.Length is 2 or 3 && upper[0] == 'F' && int.TryParse(upper[1..], out int f) && f is >= 1 and <= 24)
            return ("F" + f, 0x6F + f);
        return upper switch
        {
            "SPACE" => ("Space", 0x20),
            "PAGEUP" => ("PageUp", 0x21),
            "PAGEDOWN" => ("PageDown", 0x22),
            "END" => ("End", 0x23),
            "HOME" => ("Home", 0x24),
            "INSERT" => ("Insert", 0x2D),
            _ => null,
        };
    }
}

public sealed class QuickActionSettingsStore
{
    public const int MaxBytes = 256 * 1024;
    public string FilePath { get; }

    public QuickActionSettingsStore(string directory) =>
        FilePath = Path.Combine(Path.GetFullPath(directory), "quick-actions.json");

    /// <summary>Deep, independent copy so edits can be validated and saved before replacing live settings.</summary>
    public static QuickActionSettings Copy(QuickActionSettings settings) =>
        JsonSerializer.Deserialize<QuickActionSettings>(JsonSerializer.SerializeToUtf8Bytes(settings, Json), Json)!;

    /// <summary>Missing file = defaults (nothing written). Malformed/future files fail closed and are preserved.</summary>
    public QuickActionSettings Load() => File.Exists(FilePath) ? Read(FilePath) : QuickActionLayouts.Defaults();

    public static QuickActionSettings Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Quick actions JSON exceeds 256 KiB.");
        var settings = JsonSerializer.Deserialize<QuickActionSettings>(stream, Json) ?? throw new InvalidDataException("Empty quick actions JSON.");
        QuickActionLayouts.Validate(settings);
        return settings;
    }

    public void Save(QuickActionSettings settings)
    {
        QuickActionLayouts.Validate(settings);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(settings, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Quick actions JSON exceeds 256 KiB.");
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

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter() },
    };
}
