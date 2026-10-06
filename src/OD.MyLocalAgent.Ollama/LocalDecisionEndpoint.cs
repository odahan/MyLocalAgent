using System.Net;

namespace OD.MyLocalAgent.Ollama;

/// <summary>Validates local-only decision transports without host substring matching.</summary>
public static class LocalDecisionEndpoint
{
    /// <summary>Conservatively coalesces loopback host aliases for ownership and shared-model protection.</summary>
    public static string Identity(string endpoint)
    {
        if (!IsValid(endpoint)) throw new LocalDecisionException("NonLocalEndpoint");
        return new UriBuilder(new Uri(endpoint)) { Host = "127.0.0.1" }.Uri.AbsoluteUri;
    }

    /// <summary>Accepts loopback IP literals and the reserved localhost name only.</summary>
    public static bool IsValid(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/" &&
        (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
         (IPAddress.TryParse(uri.DnsSafeHost, out var ip) && IPAddress.IsLoopback(ip)));

    /// <summary>Validates DNS and pins localhost to a loopback literal to prevent rebinding.</summary>
    public static async Task<Uri> ResolveAsync(string endpoint, CancellationToken cancellationToken)
    {
        if (!IsValid(endpoint))
        {
            throw new LocalDecisionException("NonLocalEndpoint");
        }

        var uri = new Uri(endpoint);
        if (!uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(ip => !IPAddress.IsLoopback(ip)))
        {
            throw new LocalDecisionException("NonLocalEndpoint");
        }

        return new UriBuilder(uri) { Host = "127.0.0.1" }.Uri;
    }
}
