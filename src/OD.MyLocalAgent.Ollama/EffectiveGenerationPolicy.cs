namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Carries one fully resolved, immutable language-model call policy.
/// </summary>
/// <param name="Temperature">The effective sampling temperature.</param>
/// <param name="TopP">The effective nucleus-sampling probability mass.</param>
/// <param name="ContextWindowTokens">The declared total context-window budget.</param>
/// <param name="TemperatureSource">The source of <paramref name="Temperature"/>.</param>
/// <param name="TopPSource">The source of <paramref name="TopP"/>.</param>
public sealed record EffectiveGenerationPolicy(
    double Temperature,
    double TopP,
    int ContextWindowTokens,
    GenerationOptionSource TemperatureSource,
    GenerationOptionSource TopPSource)
{
    /// <summary>Gets whether model reasoning is enabled for this user-directed generation.</summary>
    public bool ReasoningEnabled { get; init; } = true;

    /// <summary>
    /// Gets the exact provider-specific reasoning-effort keyword, or <see langword="null"/>
    /// to use the application's provider-specific enabled default mapping.
    /// </summary>
    public string? ReasoningEffort { get; init; }
}
