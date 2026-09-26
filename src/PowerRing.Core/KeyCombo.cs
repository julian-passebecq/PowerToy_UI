namespace PowerRing.Core;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A key combination such as "Win+Tab" or "Ctrl+Alt+Shift+R". Modifier values match RegisterHotKey's MOD_* flags.</summary>
public sealed record KeyCombo(KeyModifiers Modifiers, string Key, int VirtualKey)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    /// <summary>Virtual keys of the modifiers, in press order (Ctrl, Alt, Shift, Win).</summary>
    public IReadOnlyList<int> ModifierKeys()
    {
        var keys = new List<int>();
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) keys.Add(0x11);
        if (Modifiers.HasFlag(KeyModifiers.Alt)) keys.Add(0x12);
        if (Modifiers.HasFlag(KeyModifiers.Shift)) keys.Add(0x10);
        if (Modifiers.HasFlag(KeyModifiers.Win)) keys.Add(0x5B);
        return keys;
    }

    public static KeyCombo Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64) throw new FormatException("Write a key combination such as \"Win+Tab\" or \"Ctrl+Alt+Shift+R\".");
        var modifiers = KeyModifiers.None;
        string? key = null;
        foreach (string raw in text.Split('+'))
        {
            string token = raw.Trim();
            KeyModifiers modifier = token.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => KeyModifiers.Ctrl,
                "ALT" => KeyModifiers.Alt,
                "SHIFT" => KeyModifiers.Shift,
                "WIN" or "WINDOWS" => KeyModifiers.Win,
                _ => KeyModifiers.None,
            };
            if (modifier != KeyModifiers.None)
            {
                if (modifiers.HasFlag(modifier)) throw new FormatException($"\"{token}\" appears twice in \"{text}\".");
                modifiers |= modifier;
                continue;
            }
            if (key is not null) throw new FormatException($"\"{text}\" has two keys; use one key plus modifiers.");
            key = token;
        }
        if (key is null) throw new FormatException($"\"{text}\" needs a key after the modifiers.");
        (string name, int vk) = Lookup(key) ?? throw new FormatException($"Unknown key \"{key}\" in \"{text}\". Examples: A, 5, F4, Tab, Esc, Enter, Space, Left, Delete, PrintScreen, VolumeUp.");
        return new KeyCombo(modifiers, name, vk);
    }

    /// <summary>A global hotkey needs Ctrl, Alt or Win, so normal typing is never captured.</summary>
    public static KeyCombo ParseHotkey(string? text)
    {
        KeyCombo combo = Parse(text);
        if ((combo.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Win)) == 0)
            throw new FormatException($"The hotkey \"{text}\" needs Ctrl, Alt or Win.");
        return combo;
    }

    private static (string, int)? Lookup(string key)
    {
        string upper = key.ToUpperInvariant();
        if (upper.Length == 1 && upper[0] is >= 'A' and <= 'Z' or >= '0' and <= '9') return (upper, upper[0]);
        if (upper.Length is 2 or 3 && upper[0] == 'F' && int.TryParse(upper[1..], out int f) && f is >= 1 and <= 24) return ("F" + f, 0x6F + f);
        return upper switch
        {
            "TAB" => ("Tab", 0x09),
            "ESC" or "ESCAPE" => ("Esc", 0x1B),
            "ENTER" or "RETURN" => ("Enter", 0x0D),
            "SPACE" => ("Space", 0x20),
            "BACKSPACE" => ("Backspace", 0x08),
            "DELETE" or "DEL" => ("Delete", 0x2E),
            "INSERT" => ("Insert", 0x2D),
            "HOME" => ("Home", 0x24),
            "END" => ("End", 0x23),
            "PAGEUP" => ("PageUp", 0x21),
            "PAGEDOWN" => ("PageDown", 0x22),
            "LEFT" => ("Left", 0x25),
            "UP" => ("Up", 0x26),
            "RIGHT" => ("Right", 0x27),
            "DOWN" => ("Down", 0x28),
            "PRINTSCREEN" or "PRTSC" => ("PrintScreen", 0x2C),
            "VOLUMEMUTE" => ("VolumeMute", 0xAD),
            "VOLUMEDOWN" => ("VolumeDown", 0xAE),
            "VOLUMEUP" => ("VolumeUp", 0xAF),
            "MEDIANEXT" => ("MediaNext", 0xB0),
            "MEDIAPREVIOUS" => ("MediaPrevious", 0xB1),
            "MEDIAPLAYPAUSE" => ("MediaPlayPause", 0xB3),
            _ => null,
        };
    }
}
