namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Defines OAuth client configuration for Google Workspace connectors.
/// </summary>
public sealed record GoogleOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Google";

    /// <summary>
    /// Gets the OAuth client identifier.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the OAuth client secret.
    /// </summary>
    public string ClientSecret { get; init; } = string.Empty;
}
