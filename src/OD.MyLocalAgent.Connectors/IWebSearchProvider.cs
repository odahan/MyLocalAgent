namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Implements one named provider behind the web-search connector facade.
/// </summary>
public interface IWebSearchProvider
{
    /// <summary>
    /// Gets the configuration name used to select this provider.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Searches this provider.
    /// </summary>
    /// <param name="query">The non-empty search query.</param>
    /// <param name="maxResults">The bounded maximum number of results.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized provider response.</returns>
    Task<WebSearchResult> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken);
}
