namespace JUtility.Core.Services;

public static class UrlNormalizer
{
    public static bool TryNormalizeOptionalWebUrl(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string raw = value.Trim();
        string candidate = raw.Contains("://", StringComparison.Ordinal)
            ? raw
            : "https://" + raw;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        normalized = uri.AbsoluteUri;
        return true;
    }
}
