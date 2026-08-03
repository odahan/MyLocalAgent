using System.Globalization;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Carries one immutable snapshot of the active model and its effective generation settings.
/// </summary>
public sealed record ModelPromptPlaceholderContext
{
    /// <summary>
    /// Initializes a new model-placeholder snapshot.
    /// </summary>
    /// <param name="modelName">The active model identifier.</param>
    /// <param name="generationPolicy">The effective generation policy for the call.</param>
    public ModelPromptPlaceholderContext(
        string modelName,
        EffectiveGenerationPolicy generationPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(generationPolicy);

        ModelName = modelName;
        Temperature = generationPolicy.Temperature;
        TopP = generationPolicy.TopP;
        ContextWindowTokens = generationPolicy.ContextWindowTokens;
    }

    /// <summary>
    /// Gets the active model identifier.
    /// </summary>
    public string ModelName { get; }

    /// <summary>
    /// Gets the effective sampling temperature.
    /// </summary>
    public double Temperature { get; }

    /// <summary>
    /// Gets the effective nucleus-sampling probability mass.
    /// </summary>
    public double TopP { get; }

    /// <summary>
    /// Gets the declared total context-window size in tokens.
    /// </summary>
    public int ContextWindowTokens { get; }

    /// <summary>
    /// Gets the value exposed by <c>{{ModelSettings}}</c>.
    /// </summary>
    public string ModelSettings =>
        $"Temp {Format(Temperature)} - TopP {Format(TopP)}";

    /// <summary>
    /// Gets the value exposed by <c>{{ModelContextSize}}</c>.
    /// </summary>
    public string ModelContextSize =>
        ContextWindowTokens.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the value exposed by <c>{{ModelInfo}}</c> and task-log headers.
    /// </summary>
    public string ModelInfo =>
        $"ModelName {ModelName} - {ModelSettings} - Context {ModelContextSize}";

    private static string Format(double value) =>
        value.ToString("G", CultureInfo.InvariantCulture);
}
