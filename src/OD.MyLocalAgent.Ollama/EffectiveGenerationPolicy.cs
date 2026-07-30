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
    GenerationOptionSource TopPSource);
