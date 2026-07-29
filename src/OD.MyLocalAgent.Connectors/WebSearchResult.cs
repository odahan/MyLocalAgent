namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Represents one normalized provider search response.
/// </summary>
/// <param name="Items">The normalized result items.</param>
/// <param name="Provider">The provider display name.</param>
public sealed record WebSearchResult(
    IReadOnlyList<WebSearchResultItem> Items,
    string Provider);
