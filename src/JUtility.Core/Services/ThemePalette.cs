using System.Globalization;

namespace JUtility.Core.Services;

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// Color tokens for the WPF shell. The App turns each entry into a SolidColorBrush resource
/// with the same key, so XAML only ever references keys through DynamicResource.
/// Status colors follow the Effort Board: teal = OK, amber = attention, with lighter
/// variants in dark mode so they keep contrast on dark surfaces.
/// </summary>
public static class ThemePalette
{
    public const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string RegistryValueName = "AppsUseLightTheme";

    public static IReadOnlyDictionary<string, string> Light { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PanelBrush"] = "#F7F9FC",
        ["SurfaceBrush"] = "#FFFFFF",
        ["ToolbarBrush"] = "#FBFCFE",
        ["SecondaryPanelBrush"] = "#F8FAFD",
        ["InputBrush"] = "#FFFFFF",
        ["BorderBrush"] = "#D8DEE8",
        ["DividerBrush"] = "#EEF1F5",
        ["TextBrush"] = "#1A1F29",
        ["SecondaryTextBrush"] = "#3A4455",
        ["MutedBrush"] = "#5B6577",
        ["AccentBrush"] = "#0F6CBD",
        ["OnAccentBrush"] = "#FFFFFF",
        ["AccentSoftBrush"] = "#EAF3FF",
        ["AccentSoftStrongBrush"] = "#DCEBFF",
        ["HoverBrush"] = "#F0F5FB",
        ["HoverStrongBrush"] = "#E3EDF9",
        ["NavActiveBrush"] = "#E8F2FF",
        ["NavActiveTextBrush"] = "#0B63CE",
        ["ChipBrush"] = "#EEF2F7",
        ["ControlBrush"] = "#F3F5F9",
        ["ControlHoverBrush"] = "#E6ECF4",
        ["ControlPressedBrush"] = "#D6DFEB",
        ["ControlBorderBrush"] = "#C5CDDA",
        ["ScrollThumbBrush"] = "#C2C9D4",
        ["NoteBrush"] = "#FFFBEA",
        ["NoteBorderBrush"] = "#E6DFAF",
        ["StatusOkBrush"] = "#0F6E7A",
        ["StatusAttentionBrush"] = "#B25D12",
        ["StatusIdleBrush"] = "#8A94A6",
        ["OnStatusBrush"] = "#FFFFFF",
        ["AttentionRowBrush"] = "#FDF4EA",
    };

    public static IReadOnlyDictionary<string, string> Dark { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PanelBrush"] = "#181C23",
        ["SurfaceBrush"] = "#222831",
        ["ToolbarBrush"] = "#1E232B",
        ["SecondaryPanelBrush"] = "#1C2129",
        ["InputBrush"] = "#1A1F27",
        ["BorderBrush"] = "#363E4B",
        ["DividerBrush"] = "#2C333E",
        ["TextBrush"] = "#E6EAF0",
        ["SecondaryTextBrush"] = "#C4CBD6",
        ["MutedBrush"] = "#9BA5B6",
        ["AccentBrush"] = "#5AAAF2",
        ["OnAccentBrush"] = "#0F141B",
        ["AccentSoftBrush"] = "#1D3149",
        ["AccentSoftStrongBrush"] = "#243D5C",
        ["HoverBrush"] = "#29303B",
        ["HoverStrongBrush"] = "#2F3845",
        ["NavActiveBrush"] = "#1D3149",
        ["NavActiveTextBrush"] = "#8CC4FF",
        ["ChipBrush"] = "#2C333E",
        ["ControlBrush"] = "#2A313C",
        ["ControlHoverBrush"] = "#333B48",
        ["ControlPressedBrush"] = "#3C4554",
        ["ControlBorderBrush"] = "#434C5A",
        ["ScrollThumbBrush"] = "#4A5363",
        ["NoteBrush"] = "#2E2B1E",
        ["NoteBorderBrush"] = "#575030",
        ["StatusOkBrush"] = "#3FB6C4",
        ["StatusAttentionBrush"] = "#F0A155",
        ["StatusIdleBrush"] = "#8D97A8",
        ["OnStatusBrush"] = "#14181E",
        ["AttentionRowBrush"] = "#3A2B1C",
    };

    public static IReadOnlyDictionary<string, string> For(AppTheme theme) => theme == AppTheme.Dark ? Dark : Light;

    /// <summary>
    /// Maps the raw HKCU AppsUseLightTheme registry value to a theme. Windows writes a DWORD:
    /// 0 means dark apps; anything else, or a missing value, means light.
    /// </summary>
    public static AppTheme FromAppsUseLightTheme(object? registryValue) => registryValue switch
    {
        int value => value == 0 ? AppTheme.Dark : AppTheme.Light,
        long value => value == 0 ? AppTheme.Dark : AppTheme.Light,
        _ => AppTheme.Light,
    };

    /// <summary>
    /// Parses the optional JUTILITY_THEME override ("light" / "dark"). Anything else, including
    /// "system" or an empty value, returns null so the Windows app mode is followed.
    /// </summary>
    public static AppTheme? ParseOverride(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => null,
    };

    public const string OverrideEnvironmentVariable = "JUTILITY_THEME";

    public static (byte R, byte G, byte B) ParseHex(string hex)
    {
        string digits = hex.TrimStart('#');
        if (digits.Length != 6)
        {
            throw new FormatException($"Expected #RRGGBB but got '{hex}'.");
        }

        return (
            byte.Parse(digits.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(digits.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(digits.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>WCAG 2.x contrast ratio between two #RRGGBB colors (1 to 21).</summary>
    public static double ContrastRatio(string foregroundHex, string backgroundHex)
    {
        double a = RelativeLuminance(ParseHex(foregroundHex));
        double b = RelativeLuminance(ParseHex(backgroundHex));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double RelativeLuminance((byte R, byte G, byte B) color)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
