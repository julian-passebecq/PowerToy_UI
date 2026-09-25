using System.Text.RegularExpressions;

namespace JUtility.Core.Credentials;

/// <summary>
/// OS-backed store for secret values (Windows Credential Manager in the app). Power Ops never writes these
/// values to its own files, exports, logs or UI Automation names. Targets are opaque (see CredentialRules.VaultTarget).
/// </summary>
public interface ISecretVault
{
    bool Exists(string target);
    string? ReadSecret(string target);
    void WriteSecret(string target, string secret, string userName, string comment);
    bool Delete(string target);
    IReadOnlyList<string> Targets(string prefix);
}

public static class SecretValues
{
    // CRED_MAX_CREDENTIAL_BLOB_SIZE is 5 * 512 bytes; values are stored as UTF-16.
    public const int MaxLength = 1280;

    public static void Validate(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) throw new InvalidDataException("Enter the secret value.");
        if (secret.Length > MaxLength) throw new InvalidDataException($"Windows Credential Manager holds up to {MaxLength} characters per secret.");
        if (secret.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new InvalidDataException("The secret contains control characters.");
    }
}

/// <summary>
/// Hints only. Power Ops never auto-classifies a value: the user decides whether something is secret.
/// Used to warn before a review export or when a secret-looking value is typed into a plain ID field.
/// </summary>
public static class SecretHeuristics
{
    private static readonly string[] Prefixes = ["ghp_", "gho_", "ghu_", "ghs_", "github_pat_", "sk-", "sk_live_", "rk_live_", "xoxb-", "xoxp-", "AKIA", "ASIA", "AIza", "dapi", "glpat-", "-----BEGIN"];
    private static readonly Regex Assignment = new(@"(?i)\b(password|passwd|pwd|secret|token|api[_-]?key|client[_-]?secret|private[_-]?key)\b\s*[:=]\s*\S+", RegexOptions.CultureInvariant);
    private static readonly Regex UrlWithPassword = new(@"(?i)\b[a-z][a-z0-9+.-]*://[^\s/:@]+:[^\s/@]+@", RegexOptions.CultureInvariant);
    private static readonly Regex Jwt = new(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant);
    private static readonly Regex Word = new(@"[A-Za-z0-9_\-+/=]{32,}", RegexOptions.CultureInvariant);
    private static readonly Regex HexOrGuid = new(@"^(?:[0-9a-fA-F]+|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$", RegexOptions.CultureInvariant);

    /// <summary>Null when nothing looks secret; otherwise a reason that never repeats the matched text.</summary>
    public static string? Reason(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (string prefix in Prefixes)
            if (text.Contains(prefix, StringComparison.Ordinal) && (prefix == "-----BEGIN" || Regex.IsMatch(text, Regex.Escape(prefix) + @"[A-Za-z0-9_\-]{8,}")))
                return prefix == "-----BEGIN" ? "contains a PEM key block" : "starts like a known API token";
        if (UrlWithPassword.IsMatch(text)) return "contains a URL with an embedded password";
        if (Jwt.IsMatch(text)) return "contains a JSON Web Token";
        if (Assignment.IsMatch(text)) return "contains a password/secret/token assignment";
        foreach (Match match in Word.Matches(text))
        {
            string word = match.Value;
            if (HexOrGuid.IsMatch(word)) continue; // Cloudflare/Atlas IDs are long hex strings, not secrets.
            if (word.Any(char.IsUpper) && word.Any(char.IsLower) && word.Any(char.IsDigit)) return "contains a long random-looking string";
        }
        return null;
    }
}
