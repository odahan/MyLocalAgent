using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Connectors;

internal sealed class WebSearchConnector : IWebSearchConnector
{
    private readonly IReadOnlyDictionary<string, IWebSearchProvider> _providers;
    private readonly IOptionsMonitor<WebSearchOptions> _options;

    public WebSearchConnector(
        IEnumerable<IWebSearchProvider> providers,
        IOptionsMonitor<WebSearchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);
        _providers = providers.ToDictionary(
            static provider => provider.ProviderName,
            StringComparer.OrdinalIgnoreCase);
        _options = options;
    }

    public Task<WebSearchResult> SearchAsync(
        string query,
        int maxResults,
        CancellationToken ct)
    {
        var options = GetOptions();
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            throw new WebSearchNotConfiguredException(
                "Web search is disabled. Open the Tools page and enable Web search (Ollama).");
        }

        if (!_providers.TryGetValue(options.Provider, out var provider))
        {
            throw new ConnectorConfigurationException(
                $"WebSearch:Provider '{options.Provider}' is not registered. Select a supported provider on the Tools page.");
        }

        return provider.SearchAsync(query, maxResults, ct);
    }

    private WebSearchOptions GetOptions()
    {
        try
        {
            return _options.CurrentValue;
        }
        catch (OptionsValidationException exception)
        {
            throw new ConnectorConfigurationException(
                $"Web search configuration is invalid: {exception.Message}",
                exception);
        }
    }
}
