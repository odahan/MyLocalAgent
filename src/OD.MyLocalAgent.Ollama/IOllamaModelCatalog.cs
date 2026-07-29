namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Lists models installed on an Ollama endpoint.
/// </summary>
public interface IOllamaModelCatalog
{
    /// <summary>
    /// Gets the installed model identifiers from the specified Ollama endpoint.
    /// </summary>
    /// <param name="endpoint">The Ollama HTTP endpoint.</param>
    /// <param name="timeoutSeconds">The positive request timeout in seconds.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The distinct installed model identifiers in display order.</returns>
    Task<IReadOnlyList<string>> ListInstalledModelsAsync(
        string endpoint,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
