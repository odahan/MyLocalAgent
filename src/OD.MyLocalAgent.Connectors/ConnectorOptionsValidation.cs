namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Centralizes validation rules for external connector configuration.
/// </summary>
internal static class ConnectorOptionsValidation
{
    public static string? ValidateGoogle(GoogleOptions options)
    {
        if (IsMissingOrPlaceholder(options.ClientId))
        {
            return "Google:ClientId is required. Configure an OAuth desktop client identifier.";
        }

        return IsMissingOrPlaceholder(options.ClientSecret)
            ? "Google:ClientSecret is required. Configure the OAuth desktop client secret."
            : null;
    }

    public static string? ValidateDropbox(DropboxOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AccessToken))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(options.RefreshToken) ||
               string.IsNullOrWhiteSpace(options.AppKey)
            ? "Configure Dropbox:AccessToken, or configure both Dropbox:RefreshToken and Dropbox:AppKey."
            : null;
    }

    public static string? ValidateWebSearch(WebSearchOptions options)
    {
        if (options.Provider is not ("" or WebSearchOptions.OllamaProvider))
        {
            return $"WebSearch:Provider '{options.Provider}' is not supported.";
        }

        if (options.MaxResultsCap < 1 ||
            options.DefaultMaxResults < 1 ||
            options.DefaultMaxResults > options.MaxResultsCap)
        {
            return "WebSearch result limits are invalid.";
        }

        if (options.TimeoutSeconds <= 0)
        {
            return "WebSearch:TimeoutSeconds must be positive.";
        }

        if (!string.IsNullOrWhiteSpace(options.Provider) &&
            (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) ||
             endpoint.Scheme is not ("http" or "https")))
        {
            return "WebSearch:Endpoint must be an absolute HTTP or HTTPS URL.";
        }

        return null;
    }

    private static bool IsMissingOrPlaceholder(string value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
    }
}
