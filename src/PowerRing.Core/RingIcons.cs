namespace PowerRing.Core;

/// <summary>Friendly icon names mapped to Segoe Fluent Icons code points, so a config (or an AI) never needs hex codes.</summary>
public static class RingIcons
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["app"] = "ECAA", ["apps"] = "ECAA", ["back"] = "E72B", ["bolt"] = "E945", ["book"] = "E82D", ["bug"] = "EBE8",
        ["calendar"] = "E787", ["camera"] = "E722", ["chat"] = "E8BD", ["clipboard"] = "E77F", ["cloud"] = "E753",
        ["code"] = "E943", ["copy"] = "E8C8", ["database"] = "E8F1", ["desktop"] = "E8FC", ["dev"] = "EC7A", ["document"] = "E8A5",
        ["download"] = "E896", ["downloads"] = "E896", ["edit"] = "E70F", ["explorer"] = "EC50", ["favorite"] = "E734",
        ["folder"] = "E8B7", ["game"] = "E7FC", ["globe"] = "E774", ["heart"] = "EB51", ["home"] = "E80F", ["keyboard"] = "E765",
        ["link"] = "E71B", ["lock"] = "E72E", ["mail"] = "E715", ["moon"] = "E708", ["battery"] = "E83F", ["timer"] = "E916", ["map"] = "E707", ["music"] = "E8D6", ["note"] = "E70B",
        ["person"] = "E77B", ["phone"] = "E717", ["photo"] = "EB9F", ["pin"] = "E718", ["play"] = "E768", ["power"] = "E7E8",
        ["powerops"] = "E80F", ["refresh"] = "E72C", ["screen"] = "E7F4", ["screenshot"] = "E7A8", ["search"] = "E721",
        ["settings"] = "E713", ["share"] = "E72D", ["shop"] = "E719", ["snap-left"] = "E76B", ["snap-right"] = "E76C",
        ["star"] = "E734", ["task-view"] = "E7C4", ["terminal"] = "E756", ["text"] = "E8D2", ["tools"] = "E90F",
        ["video"] = "E714", ["volume"] = "E767", ["web"] = "E774", ["windows"] = "E7C4", ["work"] = "E821",
    };

    public static IReadOnlyList<string> Names { get; } = Map.Keys.Order(StringComparer.Ordinal).ToList();

    public static bool IsKnown(string icon) => Map.ContainsKey(icon);

    public static bool IsGlyphCode(string icon) =>
        icon.Length is 4 or 5 && icon.All(Uri.IsHexDigit) && Convert.ToInt32(icon, 16) is >= 0xE000 and <= 0xF8FF;

    /// <summary>An image file, or any file/folder path (the icon Windows shows for it): "C:\...", "%LOCALAPPDATA%\...", "\\server\...".</summary>
    public static bool IsFile(string icon)
    {
        if (icon.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
        string extension = Path.GetExtension(icon).ToLowerInvariant();
        return extension is ".png" or ".ico" or ".exe" or ".jpg" or ".jpeg"
            || icon.Length > 2 && (icon[1] == ':' || icon.StartsWith('%') || icon.StartsWith(@"\\", StringComparison.Ordinal));
    }

    /// <summary>The glyph character for a name or code; null for file icons.</summary>
    public static string? Glyph(string? icon) =>
        icon is null ? null
        : Map.TryGetValue(icon, out string? code) ? char.ConvertFromUtf32(Convert.ToInt32(code, 16))
        : IsGlyphCode(icon) ? char.ConvertFromUtf32(Convert.ToInt32(icon, 16))
        : null;

    /// <summary>Icon used when an item has none: by action, and for "run" by what it starts.</summary>
    public static string Default(RingItem item) => RingActions.Of(item) switch
    {
        RingActions.Group => "folder",
        RingActions.Url => item.Target?.Contains("mail", StringComparison.OrdinalIgnoreCase) == true ? "mail" : "web",
        RingActions.Folder => item.Target?.ToLowerInvariant() switch { "downloads" => "download", "desktop" => "desktop", _ => "folder" },
        RingActions.Keys => "keyboard",
        RingActions.Text => "text",
        RingActions.Screenshot => "screenshot",
        RingActions.ScreenToClipboard => "screen",
        RingActions.PowerMode => "power",
        RingActions.CloseApps => "moon",
        RingActions.PowerOps => "powerops",
        _ => (item.Target ?? "").ToLowerInvariant() switch
        {
            var t when t.Contains("code") || t.Contains("cursor") => "code",
            var t when t.Contains("wt") || t.Contains("pwsh") || t.Contains("powershell") || t.Contains("cmd") => "terminal",
            _ => "app",
        },
    };
}
