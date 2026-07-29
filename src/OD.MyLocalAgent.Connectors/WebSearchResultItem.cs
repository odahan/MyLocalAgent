namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Represents one normalized web-search result.
/// </summary>
/// <param name="Title">The provider-supplied page title.</param>
/// <param name="Url">The provider-supplied page URL.</param>
/// <param name="Content">The provider-supplied relevant snippet.</param>
public sealed record WebSearchResultItem(string Title, string Url, string Content);
