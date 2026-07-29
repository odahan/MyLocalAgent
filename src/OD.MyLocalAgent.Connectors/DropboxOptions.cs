namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Defines credentials for the Dropbox connector.
/// </summary>
public sealed record DropboxOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Dropbox";

    /// <summary>
    /// Gets an OAuth access token, when configured.
    /// </summary>
    public string? AccessToken { get; init; }

    /// <summary>
    /// Gets an OAuth refresh token, when configured.
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Gets the Dropbox application key used to refresh tokens.
    /// </summary>
    public string? AppKey { get; init; }
}
