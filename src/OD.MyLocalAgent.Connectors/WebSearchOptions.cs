namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Configures the selected web-search provider and its request limits.
/// </summary>
public sealed record WebSearchOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "WebSearch";

    /// <summary>
    /// Gets the Ollama provider name.
    /// </summary>
    public const string OllamaProvider = "Ollama";

    /// <summary>
    /// Gets or initializes the selected provider, or an empty value when disabled.
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the provider endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "https://ollama.com/api/web_search";

    /// <summary>
    /// Gets or initializes the API key supplied through the protected secret pipeline.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Gets or initializes the default number of search results.
    /// </summary>
    public int DefaultMaxResults { get; init; } = 5;

    /// <summary>
    /// Gets or initializes the maximum accepted result count.
    /// </summary>
    public int MaxResultsCap { get; init; } = 10;

    /// <summary>
    /// Gets or initializes the request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;
}
