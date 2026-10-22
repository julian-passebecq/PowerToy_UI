namespace JUtility.Core.Actions;

/// <param name="Id">RegisterHotKey identifier (0x0000-0xBFFF for applications).</param>
public sealed record PlannedHotkey(int Id, HotkeyGesture Gesture, string ActionId);

public static class QuickActionHotkeys
{
    public const int FirstId = 0x5100;

    /// <summary>
    /// The exact set of global hotkeys to register. Empty unless the user explicitly enabled global
    /// shortcuts, so a fresh install never registers anything. Throws for invalid settings.
    /// </summary>
    public static IReadOnlyList<PlannedHotkey> Plan(QuickActionSettings settings)
    {
        QuickActionLayouts.Validate(settings);
        if (!settings.GlobalShortcutsEnabled) return [];
        return settings.GlobalShortcuts
            .Select((binding, index) => new PlannedHotkey(FirstId + index, HotkeyGesture.Parse(binding.Gesture), binding.ActionId))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>User-facing text for a failed RegisterHotKey call. 1409 = ERROR_HOTKEY_ALREADY_REGISTERED.</summary>
    public static string DescribeRegistrationFailure(HotkeyGesture gesture, int win32Error) => win32Error == 1409
        ? $"{gesture} is already used by another application or Power Ops instance. Choose a different shortcut."
        : $"Windows refused {gesture} (error {win32Error}). Choose a different shortcut.";
}

public static class QuickActionTargets
{
    private static readonly string[] TerminalCommands = ["wt", "pwsh", "powershell", "cmd"];

    /// <summary>
    /// Picks the configured terminal from the user's Tool Launcher entries: an entry named
    /// "Windows Terminal" first, otherwise the first entry whose command is a known shell.
    /// Null means nothing is configured; Power Ops never guesses an executable path.
    /// </summary>
    public static T? PickTerminal<T>(IEnumerable<T> tools, Func<T, string?> name, Func<T, string?> command) where T : class
    {
        var list = tools.ToList();
        return list.FirstOrDefault(x => string.Equals(name(x)?.Trim(), "Windows Terminal", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(command(x)))
            ?? list.FirstOrDefault(x => IsTerminalCommand(command(x)));
    }

    private static bool IsTerminalCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        string file = Path.GetFileNameWithoutExtension(command.Trim().Trim('"'));
        return TerminalCommands.Contains(file, StringComparer.OrdinalIgnoreCase);
    }
}
