namespace JUtility.Core.Services;

public static class ShellKeyboardPolicy
{
    public static bool ShouldFocusSearch(
        bool control,
        bool shift,
        bool alt,
        bool windows,
        bool searchAvailable) =>
        searchAvailable
        && control
        && !shift
        && !alt
        && !windows;

    public static bool ShouldClearSearch(
        bool escapePressed,
        bool noModifiers,
        bool searchFocused,
        bool hasSearchText) =>
        escapePressed
        && noModifiers
        && searchFocused
        && hasSearchText;
}
