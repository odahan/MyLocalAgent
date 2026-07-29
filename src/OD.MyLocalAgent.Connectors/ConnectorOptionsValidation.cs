namespace OD.MyLocalAgent.Connectors;

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

    private static bool IsMissingOrPlaceholder(string value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
    }
}
