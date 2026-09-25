using System.Windows.Media;
using Microsoft.Win32;
using PowerRing.Core;

namespace PowerRing;

/// <summary>Colours for one render: ring.json values first, then the profile accent, then the Windows theme and accent.</summary>
internal sealed class RingTheme
{
    public required bool Dark { get; init; }
    public required Color Accent { get; init; }
    public required Color Background { get; init; }
    public required Color Border { get; init; }
    public required Color Slot { get; init; }
    public required Color SlotHover { get; init; }
    public required Color Icon { get; init; }
    public required Color Text { get; init; }
    public required Color Muted { get; init; }

    public static RingTheme Resolve(RingAppearance a, RingProfile profile)
    {
        bool dark = a.Theme.ToLowerInvariant() switch { "dark" => true, "light" => false, _ => !WindowsUsesLightTheme() };
        Color accent = Parse(profile.Accent) ?? Parse(a.Accent) ?? WindowsAccent() ?? Color.FromRgb(0x3B, 0x82, 0xF6);
        Color background = Parse(a.Background) ?? (dark ? Color.FromRgb(0x20, 0x20, 0x24) : Color.FromRgb(0xF7, 0xF7, 0xF9));
        background.A = (byte)Math.Round(background.A * a.Opacity);
        return new RingTheme
        {
            Dark = dark,
            Accent = accent,
            Background = background,
            Border = Parse(a.Border) ?? (dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0x00, 0x00, 0x00)),
            Slot = Parse(a.Slot) ?? (dark ? Color.FromRgb(0x2E, 0x30, 0x36) : Color.FromRgb(0xFF, 0xFF, 0xFF)),
            SlotHover = Parse(a.SlotHover) ?? accent,
            Icon = Parse(a.Icon) ?? (dark ? Color.FromRgb(0xF3, 0xF4, 0xF6) : Color.FromRgb(0x1F, 0x23, 0x2B)),
            Text = Parse(a.Text) ?? (dark ? Color.FromRgb(0xE5, 0xE7, 0xEB) : Color.FromRgb(0x33, 0x38, 0x42)),
            Muted = dark ? Color.FromRgb(0x9C, 0xA3, 0xAF) : Color.FromRgb(0x6B, 0x72, 0x80),
        };
    }

    /// <summary>White or near-black, whichever reads better on <paramref name="background"/>.</summary>
    public static Color OnColor(Color background) =>
        0.299 * background.R + 0.587 * background.G + 0.114 * background.B > 150 ? Color.FromRgb(0x11, 0x14, 0x18) : Colors.White;

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Color? Parse(string? hex)
    {
        if (hex is null) return null;
        try { return (Color)ColorConverter.ConvertFromString(hex); } catch (FormatException) { return null; }
    }

    private static bool WindowsUsesLightTheme()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }

    private static Color? WindowsAccent()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        if (key?.GetValue("AccentColor") is not int abgr) return null;
        return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
    }
}
