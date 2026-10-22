using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using JUtility.Core.Services;
using Microsoft.Win32;

namespace JUtility.App.Services;

/// <summary>
/// Follows the Windows "app mode" (HKCU ...\Themes\Personalize AppsUseLightTheme) and swaps the
/// palette brushes in Application.Resources. XAML binds them through DynamicResource, so a swap
/// restyles the open window without reloading anything.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    private readonly Application _application;
    private readonly List<Window> _windows = [];

    public ThemeService(Application application)
    {
        _application = application;
        Current = ReadSystemTheme();
        Apply(Current);
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    public event EventHandler? ThemeChanged;

    public AppTheme Current { get; private set; }

    public static AppTheme ReadSystemTheme()
    {
        // Pinning the theme lets acceptance tests capture both palettes without touching Windows settings.
        if (ThemePalette.ParseOverride(Environment.GetEnvironmentVariable(ThemePalette.OverrideEnvironmentVariable)) is { } pinned)
        {
            return pinned;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ThemePalette.RegistryKeyPath);
            return ThemePalette.FromAppsUseLightTheme(key?.GetValue(ThemePalette.RegistryValueName));
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return AppTheme.Light;
        }
    }

    /// <summary>Keeps the native title bar in step with the palette.</summary>
    public void Track(Window window)
    {
        _windows.Add(window);
        window.SourceInitialized += (_, _) => ApplyTitleBar(window, Current);
        window.Closed += (_, _) => _windows.Remove(window);
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            ApplyTitleBar(window, Current);
        }
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Changing the app mode raises the General category (the "ImmersiveColorSet" broadcast).
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        _application.Dispatcher.BeginInvoke(() =>
        {
            AppTheme theme = ReadSystemTheme();
            if (theme == Current)
            {
                return;
            }

            Current = theme;
            Apply(theme);
            foreach (Window window in _windows)
            {
                ApplyTitleBar(window, theme);
            }

            ThemeChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void Apply(AppTheme theme)
    {
        ResourceDictionary resources = _application.Resources;
        foreach ((string key, string hex) in ThemePalette.For(theme))
        {
            (byte r, byte g, byte b) = ThemePalette.ParseHex(hex);
            SolidColorBrush brush = new(Color.FromRgb(r, g, b));
            brush.Freeze();
            resources[key] = brush;
        }
    }

    private static void ApplyTitleBar(Window window, AppTheme theme)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int enabled = theme == AppTheme.Dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            // Windows 10 builds before 20H1 used the undocumented attribute 19.
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
