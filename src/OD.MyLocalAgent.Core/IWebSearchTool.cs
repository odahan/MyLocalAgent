namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes Guardrails-protected web search to agents and workflows.
/// </summary>
public interface IWebSearchTool
{
    /// <summary>
    /// Searches the web and returns a compact protected result-set envelope.
    /// </summary>
    /// <param name="query">The non-empty search query.</param>
    /// <param name="maxResults">The optional requested result limit.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Compact JSON containing protected results and trust metadata.</returns>
    Task<string> SearchAsync(
        string query,
        int? maxResults = null,
        CancellationToken cancellationToken = default);
}
