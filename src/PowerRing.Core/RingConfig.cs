using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PowerRing.Core;

// ring.json: EVERYTHING about the ring (profiles, circles, actions, hotkey, sizes, colours, icons) lives in this one
// human- and AI-editable file. Comments and trailing commas are accepted. See ring.schema.json and RING_CONFIG.md.

public sealed class RingConfig
{
    [JsonPropertyName("$schema")] public string? Schema { get; set; } = "./ring.schema.json";
    public int Version { get; set; } = 1;
    /// <summary>Global shortcut that opens the ring at the pointer (map a mouse button to it in Logi Options+).</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+R";
    public RingAppearance Appearance { get; set; } = new();
    /// <summary>Profile shown first (id). Null = the first profile.</summary>
    public string? StartProfile { get; set; }
    public List<RingProfile> Profiles { get; set; } = [];
}

public sealed class RingAppearance
{
    /// <summary>"system" (follows the Windows app theme), "dark" or "light". Colours below override the theme.</summary>
    public string Theme { get; set; } = "system";
    /// <summary>Multiplies every size below: 0.8 = smaller ring, 1.2 = bigger. The easiest knob.</summary>
    public double Scale { get; set; } = 1;
    /// <summary>Gap between the centre and the circles, and between buttons.</summary>
    public double Spacing { get; set; } = 6;
    /// <summary>Disc diameter. Null = just big enough for every circle.</summary>
    public double? RingSize { get; set; }
    public double SlotSize { get; set; } = 42;
    public double CenterSize { get; set; } = 60;
    /// <summary>Small buttons shown next to an item that has children (its "satellites").</summary>
    public double SatelliteSize { get; set; } = 34;
    /// <summary>Buttons of the optional third circle (children of children).</summary>
    public double ThirdSize { get; set; } = 24;
    /// <summary>Distance from the centre to the middle of each slot. Null = computed from the sizes.</summary>
    public double? SlotRadius { get; set; }
    public double IconSize { get; set; } = 20;
    public double SatelliteIconSize { get; set; } = 18;
    public double ThirdIconSize { get; set; } = 12;
    public double FontSize { get; set; } = 12;
    /// <summary>Colours are "#RRGGBB" or "#AARRGGBB". Null = theme default; accent null = the Windows accent colour.</summary>
    public string? Accent { get; set; }
    public string? Background { get; set; }
    public string? Border { get; set; }
    public string? Slot { get; set; }
    public string? SlotHover { get; set; }
    public string? Icon { get; set; }
    public string? Text { get; set; }
    /// <summary>0.3-1: opacity of the disc background.</summary>
    public double Opacity { get; set; } = 1;
    public bool Shadow { get; set; } = true;
    public bool ShowNumbers { get; set; }
    public bool ShowLabels { get; set; } = true;
    /// <summary>Show the second circle (children next to their parent).</summary>
    public bool ShowSatellites { get; set; } = true;
    /// <summary>Show the third circle (children of children).</summary>
    public bool ShowThirdRing { get; set; } = true;
    /// <summary>Small workspace buttons around the centre (0-4): the other workspaces, one click away.</summary>
    public int WorkspaceButtons { get; set; } = 4;
    /// <summary>What a click on the centre does on the first circle: "board" (open the clipboard board), "home" (first workspace), "toggle" (home ⇄ board), "close".</summary>
    public string CenterClick { get; set; } = "board";
    /// <summary>Web buttons show the site's own icon, downloaded once from that site and cached next to ring.json.</summary>
    public bool WebIcons { get; set; } = true;
    /// <summary>Size of a "board" workspace (tables of clipboard, notes, links).</summary>
    public double BoardWidth { get; set; } = 560;
    public double BoardHeight { get; set; } = 420;
    /// <summary>Open/level-change animation length; 0 = none.</summary>
    public int AnimationMs { get; set; } = 120;
}

public sealed class RingProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Icon { get; set; }
    /// <summary>false hides this workspace without deleting it (tray menu > Workspaces).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Overrides the accent colour while this profile is shown.</summary>
    public string? Accent { get; set; }
    /// <summary>"ring" (circles, the default), "board" (tables: clipboard, images, notes, links) or "gallery" (app sections).</summary>
    public string? Kind { get; set; }
    public List<RingItem> Items { get; set; } = [];
    /// <summary>Board workspaces: the tables, switched with the arrows at the top.</summary>
    public List<RingTable>? Tables { get; set; }
    /// <summary>Gallery workspaces: 1-6 titled sections of app icons in rows, all visible at once (2 or 3 columns).</summary>
    public List<RingSection>? Sections { get; set; }

    [JsonIgnore] public bool IsBoard => string.Equals(Kind, "board", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsGallery => string.Equals(Kind, "gallery", StringComparison.OrdinalIgnoreCase);
    /// <summary>Board or gallery: a panel, not circles.</summary>
    [JsonIgnore] public bool IsPanel => IsBoard || IsGallery;
}

/// <summary>One table of a board workspace.</summary>
public sealed class RingTable
{
    public string Title { get; set; } = "";
    /// <summary>links (items you define), clipboard (last copied texts), images (last copied images), notes (quick notes).</summary>
    public string Kind { get; set; } = "links";
    /// <summary>1 to 3 columns.</summary>
    public int Columns { get; set; } = 2;
    /// <summary>clipboard/images: how many recent entries to keep (in memory only).</summary>
    public int? Keep { get; set; }
    /// <summary>links: any ring items (url, run, folder, text...). Right-click copies the target.</summary>
    public List<RingItem>? Items { get; set; }
}

/// <summary>One themed section of a gallery workspace ("Google", "Social", "Cloud"...).</summary>
public sealed class RingSection
{
    public string Title { get; set; } = "";
    public string? Icon { get; set; }
    public List<RingItem> Items { get; set; } = [];
}

public static class RingTableKinds
{
    public const string Links = "links", Clipboard = "clipboard", Images = "images", Notes = "notes";
    public static readonly IReadOnlyList<string> All = [Links, Clipboard, Images, Notes];
}

public sealed class RingItem
{
    public string Label { get; set; } = "";
    /// <summary>Icon name ("code", "mail"...), Segoe Fluent code point ("E943"), or a .png/.ico/.exe file path.</summary>
    public string? Icon { get; set; }
    /// <summary>Slot background colour for this item only.</summary>
    public string? Color { get; set; }
    /// <summary>run, url, folder, keys, text, screenshot, screen-to-clipboard, powerops, group. Omitted + items = group.</summary>
    public string? Action { get; set; }
    public string? Target { get; set; }
    public string? Args { get; set; }
    public string? WorkingDirectory { get; set; }
    /// <summary>screen-to-clipboard: seconds to wait first (0-10), with a countdown, e.g. 3 to open a menu.</summary>
    public int? Delay { get; set; }
    /// <summary>
    /// Children. With an action: up to 4 "satellites", small buttons shown next to this one (VS Code > its projects).
    /// Without an action (a group): a sub-circle it opens; its first children are shown as satellites too. Up to 3 levels.
    /// </summary>
    public List<RingItem>? Items { get; set; }

    [JsonIgnore] public bool IsGroup => RingActions.Of(this) == RingActions.Group;
}

public static class RingActions
{
    public const string Run = "run", Url = "url", Folder = "folder", Keys = "keys", Text = "text", Screenshot = "screenshot";
    public const string ScreenToClipboard = "screen-to-clipboard", PowerOps = "powerops", Group = "group", RingSettings = "ring-settings";
    public const string PowerMode = "power-mode", CloseApps = "close-apps";

    public static readonly IReadOnlyList<string> All = [Run, Url, Folder, Keys, Text, Screenshot, ScreenToClipboard, PowerOps, RingSettings, PowerMode, CloseApps, Group];

    /// <summary>power-mode targets: the Windows power mode (Settings > System > Power).</summary>
    public static readonly IReadOnlyList<string> PowerModes = ["efficiency", "balanced", "performance"];

    /// <summary>close-apps never closes these, whatever the list says (Power Ring itself, the shell, Claude).</summary>
    public static readonly IReadOnlyList<string> NeverClose = ["powerring", "explorer", "claude", "claude-desktop", "dwm", "csrss", "winlogon", "svchost", "system", "idle"];

    /// <summary>Process names of a close-apps target: "chrome, msedge, opera" (".exe" optional).</summary>
    public static IReadOnlyList<string> ProcessNames(string? target) =>
        (target ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x[..^4] : x)
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>ring-settings targets: edit ring.json, open its folder, reload it, or open the guide.</summary>
    public static readonly IReadOnlyList<string> SettingsTargets = ["edit", "folder", "reload", "guide"];

    public static readonly IReadOnlyList<string> SpecialFolders = ["downloads", "desktop", "documents", "pictures", "videos", "music", "home"];

    public static string Of(RingItem item) =>
        string.IsNullOrWhiteSpace(item.Action) ? (item.Items is not null ? Group : "") : item.Action.Trim().ToLowerInvariant();
}

public sealed class RingConfigException(string message) : Exception(message);

public static partial class RingConfigs
{
    public const int MaxProfiles = 6, MaxItems = 10, MaxDepth = 3, MaxSatellites = 4, MaxTables = 8, MaxTableItems = 60, MaxSections = 6, MaxSectionItems = 24;

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Parses and validates; every error names the JSON line or the field path.</summary>
    public static RingConfig Parse(string json)
    {
        RingConfig? config;
        try { config = JsonSerializer.Deserialize<RingConfig>(json, Json); }
        catch (JsonException ex)
        {
            string where = ex.LineNumber is long line ? $"line {line + 1}" + (ex.BytePositionInLine is long col ? $", column {col + 1}" : "") : "somewhere";
            string reason = ex.Message.Split(" Path:")[0].Split(" LineNumber:")[0];
            throw new RingConfigException($"ring.json is not valid JSON ({where}): {reason}");
        }
        if (config is null) throw new RingConfigException("ring.json is empty.");
        Validate(config);
        return config;
    }

    public static string Serialize(RingConfig config) => JsonSerializer.Serialize(config, Json);

    public static void Validate(RingConfig config)
    {
        if (config.Version != 1) throw Error("version", $"only version 1 is supported (found {config.Version}).");
        try { KeyCombo.ParseHotkey(config.Hotkey); } catch (FormatException ex) { throw Error("hotkey", ex.Message); }
        ValidateAppearance(config.Appearance ?? throw Error("appearance", "is missing."));
        if (config.Profiles is null || config.Profiles.Count is < 1 or > MaxProfiles)
            throw Error("profiles", $"needs 1 to {MaxProfiles} profiles.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int p = 0; p < config.Profiles.Count; p++)
        {
            RingProfile profile = config.Profiles[p] ?? throw Error($"profiles[{p}]", "is empty.");
            string path = $"profiles[{p}]";
            if (string.IsNullOrWhiteSpace(profile.Id) || !IdPattern().IsMatch(profile.Id))
                throw Error(path + ".id", "use 1-30 letters, digits or dashes (for example \"dev\").");
            if (!ids.Add(profile.Id)) throw Error(path + ".id", $"\"{profile.Id}\" is used twice.");
            if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 30) throw Error(path + ".name", "needs 1-30 characters.");
            ValidateIcon(profile.Icon, path + ".icon");
            ValidateColor(profile.Accent, path + ".accent");
            if (profile.Kind is not null && profile.Kind.ToLowerInvariant() is not ("ring" or "board" or "gallery"))
                throw Error(path + ".kind", "use \"ring\", \"board\" or \"gallery\".");
            if (profile.IsBoard) ValidateTables(profile.Tables, path + ".tables");
            else if (profile.IsGallery) ValidateSections(profile.Sections, path + ".sections");
            else ValidateItems(profile.Items, path + ".items", 1);
        }
        if (config.StartProfile is not null && !ids.Contains(config.StartProfile))
            throw Error("startProfile", $"\"{config.StartProfile}\" is not the id of a profile.");
        if (!config.Profiles.Any(x => x.Enabled)) throw Error("profiles", "at least one workspace must be enabled.");
    }

    private static void ValidateSections(List<RingSection>? sections, string path)
    {
        if (sections is null || sections.Count is < 1 or > MaxSections) throw Error(path, $"a gallery needs 1 to {MaxSections} sections.");
        for (int s = 0; s < sections.Count; s++)
        {
            RingSection section = sections[s] ?? throw Error($"{path}[{s}]", "is empty.");
            string at = $"{path}[{s}]";
            if (string.IsNullOrWhiteSpace(section.Title) || section.Title.Length > 40) throw Error(at + ".title", "needs 1-40 characters.");
            ValidateIcon(section.Icon, at + ".icon");
            if (section.Items is null || section.Items.Count is < 1 or > MaxSectionItems) throw Error(at + ".items", $"a section needs 1 to {MaxSectionItems} apps.");
            for (int i = 0; i < section.Items.Count; i++)
            {
                if (section.Items[i]?.Items is not null) throw Error($"{at}.items[{i}].items", "gallery apps cannot have children.");
                ValidateItem(section.Items[i], $"{at}.items[{i}]", MaxDepth);
            }
        }
    }

    private static void ValidateTables(List<RingTable>? tables, string path)
    {
        if (tables is null || tables.Count is < 1 or > MaxTables) throw Error(path, $"a board needs 1 to {MaxTables} tables.");
        for (int t = 0; t < tables.Count; t++)
        {
            RingTable table = tables[t] ?? throw Error($"{path}[{t}]", "is empty.");
            string at = $"{path}[{t}]";
            if (string.IsNullOrWhiteSpace(table.Title) || table.Title.Length > 40) throw Error(at + ".title", "needs 1-40 characters.");
            string kind = table.Kind?.ToLowerInvariant() ?? "";
            if (!RingTableKinds.All.Contains(kind)) throw Error(at + ".kind", $"use one of: {string.Join(", ", RingTableKinds.All)}.");
            if (table.Columns is < 1 or > 3) throw Error(at + ".columns", "use 1, 2 or 3.");
            if (table.Keep is int keep && keep is < 1 or > 50) throw Error(at + ".keep", "must be between 1 and 50.");
            if (kind == RingTableKinds.Links)
            {
                if (table.Items is null || table.Items.Count is < 1 or > MaxTableItems) throw Error(at + ".items", $"a links table needs 1 to {MaxTableItems} items.");
                for (int i = 0; i < table.Items.Count; i++)
                {
                    if (table.Items[i]?.Items is not null) throw Error($"{at}.items[{i}].items", "table items cannot have children.");
                    ValidateItem(table.Items[i], $"{at}.items[{i}]", MaxDepth);
                }
            }
            else if (table.Items is not null) throw Error(at + ".items", $"only a \"links\" table lists items (this one is \"{kind}\").");
        }
    }

    private static void ValidateAppearance(RingAppearance a)
    {
        if (a.Theme?.ToLowerInvariant() is not ("system" or "dark" or "light")) throw Error("appearance.theme", "use \"system\", \"dark\" or \"light\".");
        if (a.RingSize is double ring) Range(ring, 200, 900, "appearance.ringSize");
        Range(a.SlotSize, 28, 160, "appearance.slotSize");
        Range(a.CenterSize, 28, 200, "appearance.centerSize");
        Range(a.Scale, 0.5, 2.5, "appearance.scale");
        Range(a.Spacing, 0, 60, "appearance.spacing");
        Range(a.SatelliteSize, 16, 100, "appearance.satelliteSize");
        Range(a.ThirdSize, 12, 80, "appearance.thirdSize");
        Range(a.SatelliteIconSize, 8, 48, "appearance.satelliteIconSize");
        Range(a.ThirdIconSize, 6, 40, "appearance.thirdIconSize");
        Range(a.BoardWidth, 300, 1400, "appearance.boardWidth");
        Range(a.BoardHeight, 200, 1000, "appearance.boardHeight");
        if (a.SlotRadius is double r) Range(r, 40, 400, "appearance.slotRadius");
        Range(a.IconSize, 10, 96, "appearance.iconSize");
        Range(a.FontSize, 8, 32, "appearance.fontSize");
        Range(a.Opacity, 0.3, 1, "appearance.opacity");
        Range(a.AnimationMs, 0, 1000, "appearance.animationMs");
        Range(a.WorkspaceButtons, 0, 4, "appearance.workspaceButtons");
        if (a.CenterClick?.ToLowerInvariant() is not ("board" or "home" or "toggle" or "close"))
            throw Error("appearance.centerClick", "use \"board\", \"home\", \"toggle\" or \"close\".");
        if (a.RingSize is double size && a.SlotSize >= size / 2) throw Error("appearance.slotSize", "must be smaller than half of ringSize.");
        foreach (var (value, name) in new[] { (a.Accent, "accent"), (a.Background, "background"), (a.Border, "border"), (a.Slot, "slot"), (a.SlotHover, "slotHover"), (a.Icon, "icon"), (a.Text, "text") })
            ValidateColor(value, "appearance." + name);
    }

    private static void ValidateItems(List<RingItem>? items, string path, int depth)
    {
        if (items is null || items.Count is < 1 or > MaxItems) throw Error(path, $"needs 1 to {MaxItems} items (one circle).");
        for (int i = 0; i < items.Count; i++) ValidateItem(items[i], $"{path}[{i}]", depth);
    }

    private static void ValidateItem(RingItem? item, string at, int depth)
    {
        if (item is null) throw Error(at, "is empty.");
        if (string.IsNullOrWhiteSpace(item.Label) || item.Label.Length > 40) throw Error(at + ".label", "needs 1-40 characters.");
        ValidateIcon(item.Icon, at + ".icon");
        ValidateColor(item.Color, at + ".color");
        string action = RingActions.Of(item);
        if (!RingActions.All.Contains(action))
            throw Error(at + ".action", $"\"{item.Action}\" is unknown. Use one of: {string.Join(", ", RingActions.All)}.");
        string? target = item.Target?.Trim();
        switch (action)
        {
            case RingActions.Group:
                if (depth >= MaxDepth) throw Error(at, $"is a circle at level {depth + 1}; at most {MaxDepth} levels are allowed.");
                ValidateItems(item.Items, at + ".items", depth + 1);
                return;
            case RingActions.Run:
                if (string.IsNullOrEmpty(target)) throw Error(at + ".target", "give the program to start (for example \"code\" or a full .exe path).");
                break;
            case RingActions.Url:
                if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || !UrlSchemes.Contains(uri.Scheme))
                    throw Error(at + ".target", "give a full http(s)://, mailto: or ms-settings: address.");
                if (uri.Scheme != "mailto" && !string.IsNullOrEmpty(uri.UserInfo)) throw Error(at + ".target", "do not put a user name or password in the address.");
                break;
            case RingActions.Folder:
                if (string.IsNullOrEmpty(target)) throw Error(at + ".target", $"give a folder path or one of: {string.Join(", ", RingActions.SpecialFolders)}.");
                break;
            case RingActions.Keys:
                try { KeyCombo.Parse(target); } catch (FormatException ex) { throw Error(at + ".target", ex.Message); }
                break;
            case RingActions.Text:
                if (string.IsNullOrEmpty(item.Target) || item.Target.Length > 4000) throw Error(at + ".target", "give the text to copy (up to 4000 characters).");
                break;
            case RingActions.RingSettings:
                if (target is not null && !RingActions.SettingsTargets.Contains(target.ToLowerInvariant()))
                    throw Error(at + ".target", $"use one of: {string.Join(", ", RingActions.SettingsTargets)}.");
                break;
            case RingActions.PowerMode:
                if (target is null || !RingActions.PowerModes.Contains(target.ToLowerInvariant()))
                    throw Error(at + ".target", $"use one of: {string.Join(", ", RingActions.PowerModes)}.");
                break;
            case RingActions.CloseApps:
                IReadOnlyList<string> names = RingActions.ProcessNames(target);
                if (names.Count is < 1 or > 40 || names.Any(n => n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                    throw Error(at + ".target", "list 1 to 40 program names separated by commas, for example \"chrome, msedge, opera\".");
                if (names.FirstOrDefault(n => RingActions.NeverClose.Contains(n, StringComparer.OrdinalIgnoreCase)) is string protectedName)
                    throw Error(at + ".target", $"\"{protectedName}\" is never closed by Power Ring; remove it from the list.");
                break;
        }
        if (item.Delay is int delay && (delay is < 0 or > 10 || action != RingActions.ScreenToClipboard))
            throw Error(at + ".delay", "only screen-to-clipboard takes a delay, 0 to 10 seconds.");
        // An action with children: they are shown on the next circle, right behind it (up to 3 circles in total).
        if (item.Items is not null)
        {
            if (depth >= MaxDepth) throw Error(at + ".items", $"is on circle {depth}; at most {MaxDepth} circles are allowed.");
            if (item.Items.Count is < 1 or > MaxSatellites) throw Error(at + ".items", $"an action can have 1 to {MaxSatellites} children.");
            for (int k = 0; k < item.Items.Count; k++) ValidateItem(item.Items[k], $"{at}.items[{k}]", depth + 1);
        }
    }

    private static readonly HashSet<string> UrlSchemes = ["http", "https", "mailto", "ms-settings"];

    private static void ValidateIcon(string? icon, string path)
    {
        if (icon is null) return;
        if (RingIcons.IsKnown(icon) || RingIcons.IsGlyphCode(icon) || RingIcons.IsFile(icon)) return;
        throw Error(path, $"\"{icon}\" is not an icon name, a Segoe Fluent code (like \"E943\") or a .png/.ico/.exe path. Names: {string.Join(", ", RingIcons.Names)}.");
    }

    private static void ValidateColor(string? color, string path)
    {
        if (color is not null && !ColorPattern().IsMatch(color)) throw Error(path, $"\"{color}\" is not a colour; use \"#RRGGBB\" or \"#AARRGGBB\".");
    }

    private static void Range(double value, double min, double max, string path)
    {
        if (!double.IsFinite(value) || value < min || value > max) throw Error(path, $"must be between {min} and {max} (found {value}).");
    }

    private static RingConfigException Error(string path, string message) => new($"{path}: {message}");

    [GeneratedRegex("^[A-Za-z0-9-]{1,30}$")] private static partial Regex IdPattern();
    [GeneratedRegex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")] public static partial Regex ColorPattern();
}
