namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Defines optional reusable overrides for user-directed generation.
/// </summary>
public sealed record GenerationOptions
{
    /// <summary>
    /// Gets the optional sampling-temperature override.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// Gets the optional nucleus-sampling override.
    /// </summary>
    public double? TopP { get; init; }

    /// <summary>Gets the optional override controlling model reasoning for agentic generation.</summary>
    public bool? ReasoningEnabled { get; init; }

    /// <summary>
    /// Gets the optional provider-specific reasoning-effort keyword.
    /// </summary>
    /// <remarks>
    /// The value is deliberately open-ended because Ollama and OpenAI-compatible
    /// models do not expose one shared, discoverable vocabulary.
    /// </remarks>
    public string? ReasoningEffort { get; init; }

    /// <summary>
    /// Gets a value indicating whether neither property is overridden.
    /// </summary>
    public bool IsEmpty =>
        Temperature is null &&
        TopP is null &&
        ReasoningEnabled is null &&
        ReasoningEffort is null;
}
