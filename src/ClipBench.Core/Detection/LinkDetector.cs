namespace ClipBench.Core.Detection;

/// <summary>
/// Recognises one web address. Only <c>http</c> and <c>https</c> addresses (and <c>www.</c> ones, which are read as
/// <c>https</c>) count: <c>javascript:</c>, <c>file:</c>, <c>data:</c> and network share paths never do. Nothing is fetched
/// or resolved; the address is only read.
/// </summary>
public static class LinkDetector
{
    /// <summary>The longest address, in characters.</summary>
    public const int MaxLength = 2048;

    /// <summary>Reads the text as one web address.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address written out in full (scheme and host in lower case), or null when the text is not exactly one web address.</returns>
    public static string? TryParse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength || trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return null;
        }

        string candidate;
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = trimmed;
        }
        else if (trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "https://" + trimmed;
        }
        else
        {
            return null;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.UserInfo.Length > 0
            || !HasUsableHost(uri))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    // "https://www." and "https:///path" have no real host. A single word is only a host for localhost.
    private static bool HasUsableHost(Uri uri)
    {
        var host = uri.Host;
        if (host.Length == 0)
        {
            return false;
        }

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return true;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var labels = host.Split('.');
        return labels.Length >= 2 && labels.All(label => label.Length > 0);
    }
}
