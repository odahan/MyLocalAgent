namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Defines the reloadable application defaults for language-model generation.
/// </summary>
public sealed record AIGenerationDefaults
{
    /// <summary>
    /// Gets the default sampling temperature.
    /// </summary>
    public double Temperature { get; init; } = 0.2;

    /// <summary>
    /// Gets the default nucleus-sampling probability mass.
    /// </summary>
    public double TopP { get; init; } = 1.0;

    /// <summary>
    /// Gets the declared total context-window budget for the active model.
    /// </summary>
    public int ContextWindowTokens { get; init; } = 8192;
}
