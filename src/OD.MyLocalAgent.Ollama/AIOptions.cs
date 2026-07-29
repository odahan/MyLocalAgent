namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Defines the active language-model provider.
/// </summary>
public sealed record AIOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "AI";

    /// <summary>
    /// Gets the Ollama provider name.
    /// </summary>
    public const string OllamaProvider = "Ollama";

    /// <summary>
    /// Gets the OpenAI provider name.
    /// </summary>
    public const string OpenAIProvider = "OpenAI";

    /// <summary>
    /// Gets the configured provider.
    /// </summary>
    public string Provider { get; init; } = OllamaProvider;
}
