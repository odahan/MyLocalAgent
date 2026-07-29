namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Provides a stable provider-independent web-search boundary.
/// </summary>
public interface IWebSearchConnector
{
    /// <summary>
    /// Searches the selected provider.
    /// </summary>
    /// <param name="query">The non-empty search query.</param>
    /// <param name="maxResults">The bounded maximum number of results.</param>
    /// <param name="ct">A token that cancels the operation.</param>
    /// <returns>The normalized provider response.</returns>
    Task<WebSearchResult> SearchAsync(
        string query,
        int maxResults,
        CancellationToken ct);
}
