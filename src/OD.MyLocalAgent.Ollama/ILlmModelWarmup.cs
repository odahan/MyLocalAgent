namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Prepares a local language model for an imminent task execution.
/// </summary>
public interface ILlmModelWarmup
{
    /// <summary>
    /// Loads the configured local model and verifies that it can generate a response.
    /// </summary>
    /// <param name="policy">The generation policy that will be used by the task.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task WarmUpAsync(
        EffectiveGenerationPolicy policy,
        CancellationToken cancellationToken);
}
