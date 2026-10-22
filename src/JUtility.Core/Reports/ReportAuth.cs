using System.Net.Http.Headers;
using System.Text;

namespace JUtility.Core.Reports;

// Sign-in for a protected Mongoku (MONGOKU_AUTH_BASIC). The user name and password live in the Windows
// Credential Manager (per Windows user, DPAPI-protected), keyed by Mongoku origin - never in Power Ops JSON,
// logs or exports. They are sent only to that exact origin, only over https or to this computer.

public sealed record BasicCredential(string UserName, string Password)
{
    // Records print every property by default; never let a password reach a log or a message box.
    public override string ToString() => $"BasicCredential {{ UserName = {UserName}, Password = *** }}";
}

public interface ICredentialVault
{
    BasicCredential? Read(string target);
    void Write(string target, BasicCredential credential);
    bool Delete(string target);
}

public static class ReportAuth
{
    public const string TargetPrefix = "PowerOps/Mongoku/";

    /// <summary>Credential Manager target for a Mongoku address: one entry per origin (scheme, host and port).</summary>
    public static string Target(string sourceUrl) => TargetPrefix + Origin(new Uri(sourceUrl.Trim()));

    public static string Origin(Uri uri) => uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();

    public static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>Null when a password may be sent to this address; otherwise why not.</summary>
    public static string? RefusalToSend(Uri source) =>
        source.Scheme == Uri.UriSchemeHttps || (source.Scheme == Uri.UriSchemeHttp && IsLoopback(source))
            ? null
            : $"Power Ops only sends a Mongoku password over https or to this computer (localhost); {Origin(source)} uses plain http.";

    public static void Validate(BasicCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.UserName) || credential.UserName.Length > 128 || credential.UserName.Contains(':'))
            throw new InvalidDataException("Enter a user name (up to 128 characters, no ':').");
        if (string.IsNullOrEmpty(credential.Password) || credential.Password.Length > 256)
            throw new InvalidDataException("Enter a password (up to 256 characters).");
        if ((credential.UserName + credential.Password).Any(char.IsControl))
            throw new InvalidDataException("User name and password cannot contain control characters.");
    }

    public static AuthenticationHeaderValue Header(BasicCredential credential) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credential.UserName}:{credential.Password}")));
}
