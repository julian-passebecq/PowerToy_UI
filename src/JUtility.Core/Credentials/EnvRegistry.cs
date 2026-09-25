namespace JUtility.Core.Credentials;

/// <summary>A key NAME seen in a .env file. There is deliberately no value field.</summary>
public sealed record EnvKeyObservation(string Name, bool HasValue, int Line);

/// <summary>Warnings name line numbers only, never file content.</summary>
public sealed record EnvInspection(IReadOnlyList<EnvKeyObservation> Keys, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads .env files for key names and present/empty state. Values are parsed only far enough to know whether
/// they are empty and are then discarded: nothing returned or persisted contains them. Never writes .env files.
/// </summary>
public static class EnvRegistry
{
    public const int MaxFileBytes = 1024 * 1024, MaxDiscovered = 50;

    public static bool IsEnvFileName(string fileName) =>
        fileName.Equals(".env", StringComparison.OrdinalIgnoreCase)
        || (fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) && fileName.Length > 5)
        || fileName.EndsWith(".env", StringComparison.OrdinalIgnoreCase) && fileName.Length > 4 && !fileName.Contains(' ');

    /// <summary>Environment label suggested by the file name: .env.local -> local, .env.preview -> preview, .env -> local.</summary>
    public static string SuggestEnvironment(string fileName)
    {
        string name = Path.GetFileName(fileName);
        if (name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)) return name[5..].ToLowerInvariant() is { Length: > 0 and <= 40 } label ? label : "local";
        if (name.EndsWith(".env", StringComparison.OrdinalIgnoreCase) && name.Length > 4) return name[..^4].TrimEnd('.').ToLowerInvariant() is { Length: > 0 and <= 40 } prefix ? prefix : "local";
        return "local";
    }

    public static EnvInspection Inspect(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxFileBytes) throw new InvalidDataException("The .env file is larger than 1 MiB; Power Ops did not read it.");
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return Parse(reader);
    }

    public static EnvInspection Parse(TextReader reader)
    {
        var keys = new List<EnvKeyObservation>();
        var warnings = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        int lineNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;
            if (trimmed.StartsWith("export ", StringComparison.Ordinal)) trimmed = trimmed[7..].TrimStart();
            int equals = trimmed.IndexOf('=');
            if (equals <= 0)
            {
                warnings.Add($"Line {lineNumber} is not KEY=VALUE and was ignored.");
                continue;
            }
            string name = trimmed[..equals].Trim();
            if (!CredentialRules.IsValidKeyName(name))
            {
                warnings.Add($"Line {lineNumber} has an unsupported key name and was ignored.");
                continue;
            }
            int declaredAt = lineNumber;
            string rest = trimmed[(equals + 1)..].TrimStart();
            bool hasValue;
            if (rest.Length > 0 && rest[0] is '"' or '\'' or '`')
            {
                char quote = rest[0];
                int close = ClosingQuote(rest, 1, quote);
                if (close >= 0)
                {
                    hasValue = close > 1;
                }
                else
                {
                    // Multi-line quoted value: consume lines until the closing quote without keeping them.
                    hasValue = rest.Length > 1;
                    bool closed = false;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        lineNumber++;
                        if (line.Length > 0) hasValue = true;
                        if (ClosingQuote(line, 0, quote) >= 0) { closed = true; break; }
                    }
                    if (!closed) warnings.Add($"The quoted value starting on line {declaredAt} is never closed.");
                }
            }
            else
            {
                // Unquoted: " #" starts an inline comment; a value that is only a comment is empty.
                int comment = rest.StartsWith('#') ? 0 : rest.IndexOf(" #", StringComparison.Ordinal);
                hasValue = (comment >= 0 ? rest[..comment] : rest).Trim().Length > 0;
            }

            if (seen.TryGetValue(name, out int index))
            {
                warnings.Add($"{name} is declared again on line {declaredAt}; the last declaration wins.");
                keys[index] = new EnvKeyObservation(name, hasValue, declaredAt);
            }
            else
            {
                seen[name] = keys.Count;
                keys.Add(new EnvKeyObservation(name, hasValue, declaredAt));
            }
            if (keys.Count > CredentialRules.MaxKeysPerFile)
            {
                warnings.Add($"More than {CredentialRules.MaxKeysPerFile} keys; the rest were not inspected.");
                keys.RemoveAt(keys.Count - 1);
                break;
            }
        }
        return new EnvInspection(keys, warnings);
    }

    private static int ClosingQuote(string text, int start, char quote)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '\\' && quote == '"') { i++; continue; }
            if (text[i] == quote) return i;
        }
        return -1;
    }

    /// <summary>
    /// Updates key states from a fresh inspection. Keys the user expects or linked to a credential stay listed
    /// (as Missing when gone); keys that were merely observed and disappeared are dropped.
    /// </summary>
    public static void Apply(EnvFileEntry entry, EnvInspection inspection, DateTimeOffset observedUtc)
    {
        var observed = inspection.Keys.ToDictionary(x => x.Name, StringComparer.Ordinal);
        entry.Keys.RemoveAll(x => !observed.ContainsKey(x.Name) && !x.Expected && x.CredentialRef is null);
        foreach (var key in entry.Keys)
            key.State = observed.TryGetValue(key.Name, out var seen) ? (seen.HasValue ? EnvKeyState.Present : EnvKeyState.Empty) : EnvKeyState.Missing;
        foreach (var seen in inspection.Keys)
        {
            if (entry.Keys.Any(x => x.Name == seen.Name)) continue;
            if (entry.Keys.Count >= CredentialRules.MaxKeysPerFile) break;
            entry.Keys.Add(new EnvKeyEntry { Name = seen.Name, State = seen.HasValue ? EnvKeyState.Present : EnvKeyState.Empty });
        }
        entry.ObservedUtc = observedUtc;
        entry.ObservationProblem = "";
    }

    /// <summary>Records that the file could not be read; previous key states are kept but marked Unknown.</summary>
    public static void MarkUnreadable(EnvFileEntry entry, string problem)
    {
        foreach (var key in entry.Keys) key.State = EnvKeyState.Unknown;
        entry.ObservationProblem = problem.Length > 300 ? problem[..300] : problem;
    }

    /// <summary>
    /// KEY=value line for the user to paste into a .env (clipboard only; Power Ops never writes .env files).
    /// Values with spaces, quotes, '#' or line breaks are double-quoted with escapes.
    /// </summary>
    public static string FormatAssignment(string name, string value)
    {
        if (!CredentialRules.IsValidKeyName(name)) throw new InvalidDataException("Invalid .env key name.");
        bool quote = value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '#' or '\\' or '`' or '$');
        if (!quote) return name + "=" + value;
        string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        return name + "=\"" + escaped + "\"";
    }

    /// <summary>.env files directly inside a folder (not recursive), bounded.</summary>
    public static IReadOnlyList<string> Discover(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(x => IsEnvFileName(Path.GetFileName(x)))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(MaxDiscovered)
            .ToList();
}
