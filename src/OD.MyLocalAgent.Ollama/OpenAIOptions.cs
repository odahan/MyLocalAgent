namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Defines configuration for the opt-in OpenAI-compatible provider.
/// </summary>
public sealed record OpenAIOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "OpenAI";

    /// <summary>
    /// Gets the OpenAI-compatible API endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "https://api.openai.com/v1";

    /// <summary>
    /// Gets the model identifier.
    /// </summary>
    public string Model { get; init; } = "gpt-4o-mini";

    /// <summary>
    /// Gets the request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// Gets the API key supplied by the bootstrap configuration layer.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>Gets whether the configured endpoint explicitly supports image input.</summary>
    public bool SupportsImageInput { get; init; }

    /// <summary>Gets whether the configured endpoint explicitly supports the Image API.</summary>
    public bool SupportsImageOutput { get; init; }

    /// <summary>Gets whether the configured endpoint explicitly supports the Speech API.</summary>
    public bool SupportsAudioOutput { get; init; }

    /// <summary>Gets the model sent only to the image-generation endpoint.</summary>
    public string ImageModel { get; init; } = "gpt-image-2";

    /// <summary>Gets the model sent only to the speech-generation endpoint.</summary>
    public string AudioModel { get; init; } = "gpt-4o-mini-tts";

    /// <summary>Gets the voice sent only to the speech-generation endpoint.</summary>
    public string AudioVoice { get; init; } = "coral";
}
