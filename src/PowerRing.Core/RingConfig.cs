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
    public double RingSize { get; set; } = 340;
    public double SlotSize { get; set; } = 60;
    public double CenterSize { get; set; } = 78;
    /// <summary>Distance from the centre to the middle of each slot. Null = computed from the sizes.</summary>
    public double? SlotRadius { get; set; }
    public double IconSize { get; set; } = 24;
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
    public double Opacity { get; set; } = 0.94;
    public bool Shadow { get; set; } = true;
    public bool ShowNumbers { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
    /// <summary>Open/level-change animation length; 0 = none.</summary>
    public int AnimationMs { get; set; } = 120;
}

public sealed class RingProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Icon { get; set; }
    /// <summary>Overrides the accent colour while this profile is shown.</summary>
    public string? Accent { get; set; }
    public List<RingItem> Items { get; set; } = [];
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
    /// <summary>Sub-circle (action "group"). Up to 3 levels in total.</summary>
    public List<RingItem>? Items { get; set; }

    [JsonIgnore] public bool IsGroup => RingActions.Of(this) == RingActions.Group;
}

public static class RingActions
{
    public const string Run = "run", Url = "url", Folder = "folder", Keys = "keys", Text = "text", Screenshot = "screenshot";
    public const string ScreenToClipboard = "screen-to-clipboard", PowerOps = "powerops", Group = "group";

    public static readonly IReadOnlyList<string> All = [Run, Url, Folder, Keys, Text, Screenshot, ScreenToClipboard, PowerOps, Group];

    public static readonly IReadOnlyList<string> SpecialFolders = ["downloads", "desktop", "documents", "pictures", "videos", "music", "home"];

    public static string Of(RingItem item) =>
        string.IsNullOrWhiteSpace(item.Action) ? (item.Items is not null ? Group : "") : item.Action.Trim().ToLowerInvariant();
}

public sealed class RingConfigException(string message) : Exception(message);

public static partial class RingConfigs
{
    public const int MaxProfiles = 5, MaxItems = 8, MaxDepth = 3;

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
            ValidateItems(profile.Items, path + ".items", 1);
        }
        if (config.StartProfile is not null && !ids.Contains(config.StartProfile))
            throw Error("startProfile", $"\"{config.StartProfile}\" is not the id of a profile.");
    }

    private static void ValidateAppearance(RingAppearance a)
    {
        if (a.Theme?.ToLowerInvariant() is not ("system" or "dark" or "light")) throw Error("appearance.theme", "use \"system\", \"dark\" or \"light\".");
        Range(a.RingSize, 200, 800, "appearance.ringSize");
        Range(a.SlotSize, 28, 160, "appearance.slotSize");
        Range(a.CenterSize, 28, 200, "appearance.centerSize");
        if (a.SlotRadius is double r) Range(r, 40, 400, "appearance.slotRadius");
        Range(a.IconSize, 10, 96, "appearance.iconSize");
        Range(a.FontSize, 8, 32, "appearance.fontSize");
        Range(a.Opacity, 0.3, 1, "appearance.opacity");
        Range(a.AnimationMs, 0, 1000, "appearance.animationMs");
        if (a.SlotSize >= a.RingSize / 2) throw Error("appearance.slotSize", "must be smaller than half of ringSize.");
        foreach (var (value, name) in new[] { (a.Accent, "accent"), (a.Background, "background"), (a.Border, "border"), (a.Slot, "slot"), (a.SlotHover, "slotHover"), (a.Icon, "icon"), (a.Text, "text") })
            ValidateColor(value, "appearance." + name);
    }

    private static void ValidateItems(List<RingItem>? items, string path, int depth)
    {
        if (items is null || items.Count is < 1 or > MaxItems) throw Error(path, $"needs 1 to {MaxItems} items (one circle).");
        for (int i = 0; i < items.Count; i++)
        {
            RingItem item = items[i] ?? throw Error($"{path}[{i}]", "is empty.");
            string at = $"{path}[{i}]";
            if (string.IsNullOrWhiteSpace(item.Label) || item.Label.Length > 40) throw Error(at + ".label", "needs 1-40 characters.");
            ValidateIcon(item.Icon, at + ".icon");
            ValidateColor(item.Color, at + ".color");
            string action = RingActions.Of(item);
            if (!RingActions.All.Contains(action))
                throw Error(at + ".action", $"\"{item.Action}\" is unknown. Use one of: {string.Join(", ", RingActions.All)}.");
            if (action != RingActions.Group && item.Items is not null) throw Error(at + ".items", $"only a \"group\" can have items (this one is \"{action}\").");
            string? target = item.Target?.Trim();
            switch (action)
            {
                case RingActions.Group:
                    if (depth >= MaxDepth) throw Error(at, $"is a circle at level {depth + 1}; at most {MaxDepth} levels are allowed.");
                    ValidateItems(item.Items, at + ".items", depth + 1);
                    break;
                case RingActions.Run:
                    if (string.IsNullOrEmpty(target)) throw Error(at + ".target", "give the program to start (for example \"code\" or a full .exe path).");
                    break;
                case RingActions.Url:
                    if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https" or "mailto"))
                        throw Error(at + ".target", "give a full http(s):// or mailto: address.");
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
            }
        }
    }

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
