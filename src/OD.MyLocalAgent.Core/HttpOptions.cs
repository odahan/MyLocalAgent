namespace OD.MyLocalAgent.Core;

/// <summary>
/// Configures outbound HTTP tool requests.
/// </summary>
public sealed class HttpOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Http";

    /// <summary>
    /// Gets or sets the request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the user-agent value sent by the HTTP tool.
    /// </summary>
    public string UserAgent { get; set; } = "OD.MyLocalAgent/1.0";

    /// <summary>
    /// Gets or sets the optional exact host allowlist. An empty list allows every host.
    /// </summary>
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum number of response characters returned to the model.
    /// </summary>
    public int MaxResponseCharacters { get; set; } = 100_000;
}
