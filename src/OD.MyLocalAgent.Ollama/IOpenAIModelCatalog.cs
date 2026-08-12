namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Lists models exposed by an OpenAI-compatible endpoint.
/// </summary>
public interface IOpenAIModelCatalog
{
    /// <summary>
    /// Gets model identifiers from the specified OpenAI-compatible endpoint.
    /// </summary>
    /// <param name="endpoint">The provider HTTP endpoint.</param>
    /// <param name="timeoutSeconds">The positive request timeout in seconds.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The distinct model identifiers in display order.</returns>
    Task<IReadOnlyList<string>> ListModelsAsync(
        string endpoint,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
