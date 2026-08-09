using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Deterministically normalizes URLs for deduplication and canonical identification.
/// </summary>
internal static class UrlCanonicalizer
{
    private static readonly HashSet<string> NonSemanticParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source",
        "utm_medium",
        "utm_campaign",
        "utm_term",
        "utm_content",
        "fbclid",
        "gclid",
        "mc_cid",
        "mc_eid",
        "ref",
        "source",
    };

    /// <summary>
    /// Canonicalizes a URL by removing fragments, tracking parameters,
    /// normalizing host casing, and default ports.
    /// </summary>
    /// <param name="url">The input URL.</param>
    /// <returns>The canonical URL string.</returns>
    public static string Canonicalize(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var builder = new StringBuilder();
        builder.Append(uri.Scheme.ToLowerInvariant());
        builder.Append("://");
        builder.Append(uri.IdnHost.ToLowerInvariant());

        if (!IsDefaultPort(uri))
        {
            builder.Append(':');
            builder.Append(uri.Port);
        }

        builder.Append(NormalizePath(uri.AbsolutePath));

        if (!string.IsNullOrEmpty(uri.Query))
        {
            var filteredQuery = FilterQueryParameters(uri.Query);
            if (!string.IsNullOrEmpty(filteredQuery))
            {
                builder.Append(filteredQuery);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Extracts the domain from a URL string.
    /// </summary>
    /// <param name="url">The URL string.</param>
    /// <returns>The domain, or an empty string if parsing fails.</returns>
    public static string ExtractDomain(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.IdnHost.ToLowerInvariant();
        }

        return string.Empty;
    }

    private static bool IsDefaultPort(Uri uri)
    {
        return uri switch
        {
            { Scheme: "http", Port: 80 } => true,
            { Scheme: "https", Port: 443 } => true,
            _ => false,
        };
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return "/";
        }

        // Decode percent-encoded characters that don't need encoding, then re-encode
        var decoded = Uri.UnescapeDataString(path);
        var normalized = decoded.Replace("//", "/", StringComparison.Ordinal);

        // Ensure path starts with /
        if (normalized.Length == 0 || normalized[0] != '/')
        {
            normalized = "/" + normalized;
        }

        return normalized;
    }

    private static string FilterQueryParameters(string query)
    {
        if (string.IsNullOrEmpty(query) || query == "?")
        {
            return string.Empty;
        }

        var paramsStr = query.TrimStart('?');
        var parameters = paramsStr.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var filtered = new List<string>();

        foreach (var param in parameters)
        {
            var eqIndex = param.IndexOf('=');
            var key = eqIndex >= 0 ? param[..eqIndex] : param;

            if (!NonSemanticParams.Contains(key))
            {
                filtered.Add(param);
            }
        }

        if (filtered.Count == 0)
        {
            return string.Empty;
        }

        return "?" + string.Join("&", filtered);
    }
}
