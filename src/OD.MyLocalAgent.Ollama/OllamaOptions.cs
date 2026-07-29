namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Defines configuration for the local Ollama provider.
/// </summary>
public sealed record OllamaOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Ollama";

    /// <summary>
    /// Gets the Ollama API endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "http://localhost:11434";

    /// <summary>
    /// Gets the model identifier.
    /// </summary>
    public string Model { get; init; } = "gemma4:12b";

    /// <summary>
    /// Gets the request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 300;
}
