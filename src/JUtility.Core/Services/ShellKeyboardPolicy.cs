namespace JUtility.Core.Services;

public static class ShellKeyboardPolicy
{
    public static bool ShouldFocusSearch(
        bool control,
        bool shift,
        bool alt,
        bool windows,
        bool searchAvailable,
        bool editingText) =>
        searchAvailable
        && !editingText
        && control
        && !shift
        && !alt
        && !windows;

    public static bool ShouldOpenExplorer(
        bool control,
        bool shift,
        bool alt,
        bool windows,
        bool editingText) =>
        control
        && shift
        && !alt
        && !windows
        && !editingText;

    public static bool ShouldClearSearch(
        bool escapePressed,
        bool noModifiers,
        bool searchFocused,
        bool hasSearchText) =>
        escapePressed
        && noModifiers
        && searchFocused
        && hasSearchText;
    public static bool ShouldActivateListItem(
        bool enterPressed,
        bool noModifiers) =>
        enterPressed
        && noModifiers;

    public static bool ShouldCopyListItem(
        bool cPressed,
        bool control,
        bool shift,
        bool alt,
        bool windows) =>
        cPressed
        && control
        && !shift
        && !alt
        && !windows;
}
