using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Calls the Ollama cloud Web Search REST API.
/// </summary>
public sealed class OllamaWebSearchConnector : IWebSearchProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<WebSearchOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="OllamaWebSearchConnector"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The managed HTTP client factory.</param>
    /// <param name="options">The reloadable web-search options.</param>
    public OllamaWebSearchConnector(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<WebSearchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    /// <inheritdoc />
    public string ProviderName => WebSearchOptions.OllamaProvider;

    /// <inheritdoc />
    public async Task<WebSearchResult> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var options = GetValidatedOptions();
        if (maxResults < 1 || maxResults > options.MaxResultsCap)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                $"Web search result count must be between 1 and {options.MaxResultsCap}.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = JsonContent.Create(
                new
                {
                    query,
                    max_results = maxResults,
                }),
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", options.ApiKey);

        try
        {
            var client = _httpClientFactory.CreateClient(
                ConnectorHttpClientNames.WebSearch);
            using var response = await client
                .SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateStatusException(response.StatusCode);
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new WebSearchResult(
                ParseItems(document.RootElement, maxResults),
                ProviderName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WebSearchApiException)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new WebSearchApiException(
                null,
                $"Ollama Web Search timed out after {options.TimeoutSeconds} seconds.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new WebSearchApiException(
                (int)HttpStatusCode.OK,
                "Ollama Web Search returned malformed JSON.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new WebSearchApiException(
                exception.StatusCode is null
                    ? null
                    : (int)exception.StatusCode.Value,
                $"Ollama Web Search could not be reached: {exception.Message}",
                exception);
        }
    }

    private WebSearchOptions GetValidatedOptions()
    {
        WebSearchOptions options;
        try
        {
            options = _options.CurrentValue;
        }
        catch (OptionsValidationException exception)
        {
            throw new ConnectorConfigurationException(
                $"Web search configuration is invalid: {exception.Message}",
                exception);
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new ConnectorConfigurationException(
                "WebSearch:ApiKey is missing. Add the Ollama Web Search API key on the Tools page.");
        }

        return options;
    }

    private static IReadOnlyList<WebSearchResultItem> ParseItems(
        JsonElement root,
        int maxResults)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<WebSearchResultItem>(maxResults);
        foreach (var element in results.EnumerateArray())
        {
            if (items.Count >= maxResults)
            {
                break;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(
                new WebSearchResultItem(
                    ReadOptionalString(element, "title"),
                    ReadOptionalString(element, "url"),
                    ReadOptionalString(element, "content")));
        }

        return items;
    }

    private static string ReadOptionalString(
        JsonElement element,
        string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static WebSearchApiException CreateStatusException(
        HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        var message = statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? $"Ollama Web Search returned HTTP {code}; the API key is likely missing or invalid."
            : $"Ollama Web Search returned HTTP {code}.";
        return new WebSearchApiException(code, message);
    }
}
