using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies provider selection and the Ollama Web Search HTTP contract.
/// </summary>
public sealed class WebSearchConnectorTests
{
    /// <summary>
    /// Verifies the documented Ollama request and normalized response.
    /// </summary>
    [Fact]
    public async Task OllamaProviderMapsDocumentedRequestAndResponse()
    {
        HttpMethod? method = null;
        Uri? uri = null;
        string? authenticationScheme = null;
        string? authenticationParameter = null;
        string? requestBody = null;
        var responseBody = await File.ReadAllTextAsync(
            FixturePath("web-search-normal.json"));
        var handler = new StubHttpMessageHandler(
            async (request, _) =>
            {
                method = request.Method;
                uri = request.RequestUri;
                authenticationScheme = request.Headers.Authorization?.Scheme;
                authenticationParameter =
                    request.Headers.Authorization?.Parameter;
                requestBody = await request.Content!.ReadAsStringAsync();
                return JsonResponse(HttpStatusCode.OK, responseBody);
            });
        var connector = CreateOllamaProvider(handler);

        var result = await connector.SearchAsync(
            "current Ollama documentation",
            2,
            CancellationToken.None);

        method.Should().Be(HttpMethod.Post);
        uri.Should().Be(new Uri("https://ollama.com/api/web_search"));
        authenticationScheme.Should().Be("Bearer");
        authenticationParameter.Should().Be("test-secret");
        using var requestDocument = JsonDocument.Parse(requestBody!);
        requestDocument.RootElement.GetProperty("query").GetString()
            .Should().Be("current Ollama documentation");
        requestDocument.RootElement.GetProperty("max_results").GetInt32()
            .Should().Be(2);
        result.Provider.Should().Be(WebSearchOptions.OllamaProvider);
        result.Items.Should().HaveCount(2);
        result.Items[0].Title.Should().Be("Ollama Web Search");
        result.Items[0].Url.Should()
            .Be("https://docs.ollama.com/capabilities/web-search");
    }

    /// <summary>
    /// Verifies optional and unknown response fields are tolerated.
    /// </summary>
    [Theory]
    [InlineData("""{"unexpected":true}""", 0)]
    [InlineData("""{"results":"not-an-array"}""", 0)]
    [InlineData(
        """{"results":[{"title":"Title","unknown":42},{"url":7},null]}""",
        2)]
    public async Task OllamaProviderToleratesOptionalAndUnknownFields(
        string responseBody,
        int expectedCount)
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                JsonResponse(HttpStatusCode.OK, responseBody)));
        var connector = CreateOllamaProvider(handler);

        var result = await connector.SearchAsync(
            "query",
            10,
            CancellationToken.None);

        result.Items.Should().HaveCount(expectedCount);
        if (expectedCount > 0)
        {
            result.Items[0].Title.Should().Be("Title");
            result.Items[0].Url.Should().BeEmpty();
            result.Items[0].Content.Should().BeEmpty();
        }
    }

    /// <summary>
    /// Verifies malformed provider JSON is surfaced as a typed failure.
    /// </summary>
    [Fact]
    public async Task OllamaProviderRejectsMalformedJson()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                JsonResponse(HttpStatusCode.OK, "{not-json")));
        var connector = CreateOllamaProvider(handler);

        var action = () => connector.SearchAsync(
            "query",
            5,
            CancellationToken.None);

        await action.Should().ThrowAsync<WebSearchApiException>()
            .Where(exception =>
                exception.StatusCode == 200 &&
                exception.Message.Contains(
                    "malformed JSON",
                    StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies authentication failures give actionable key guidance.
    /// </summary>
    [Fact]
    public async Task OllamaProviderExplainsAuthenticationFailure()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                JsonResponse(HttpStatusCode.Unauthorized, "{}")));
        var connector = CreateOllamaProvider(handler);

        var action = () => connector.SearchAsync(
            "query",
            5,
            CancellationToken.None);

        await action.Should().ThrowAsync<WebSearchApiException>()
            .Where(exception =>
                exception.StatusCode == 401 &&
                exception.Message.Contains(
                    "API key",
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies transport failures are wrapped in the typed provider exception.
    /// </summary>
    [Fact]
    public async Task OllamaProviderWrapsTransportFailure()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new HttpRequestException("network unavailable"));
        var connector = CreateOllamaProvider(handler);

        var action = () => connector.SearchAsync(
            "query",
            5,
            CancellationToken.None);

        await action.Should().ThrowAsync<WebSearchApiException>()
            .Where(exception =>
                exception.StatusCode == null &&
                exception.InnerException != null &&
                exception.InnerException.GetType() == typeof(HttpRequestException) &&
                exception.Message.Contains(
                    "could not be reached",
                    StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies missing keys fail before any provider request.
    /// </summary>
    [Fact]
    public async Task OllamaProviderRequiresApiKey()
    {
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(
            (_, _) =>
            {
                requestCount++;
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}"));
            });
        var options = ValidOptions() with { ApiKey = " " };
        var connector = new OllamaWebSearchConnector(
            new StubHttpClientFactory(handler),
            new MutableOptionsMonitor<WebSearchOptions>(options));

        var action = () => connector.SearchAsync(
            "query",
            5,
            CancellationToken.None);

        await action.Should().ThrowAsync<ConnectorConfigurationException>()
            .WithMessage("*WebSearch:ApiKey*");
        requestCount.Should().Be(0);
    }

    /// <summary>
    /// Verifies provider timeout is distinct from caller cancellation.
    /// </summary>
    [Fact]
    public async Task OllamaProviderWrapsHttpClientTimeout()
    {
        var handler = new StubHttpMessageHandler(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return JsonResponse(HttpStatusCode.OK, "{}");
            });
        var connector = CreateOllamaProvider(
            handler,
            TimeSpan.FromMilliseconds(20));

        var action = () => connector.SearchAsync(
            "query",
            5,
            CancellationToken.None);

        await action.Should().ThrowAsync<WebSearchApiException>()
            .WithMessage("*timed out*");
    }

    /// <summary>
    /// Verifies explicit caller cancellation is propagated unchanged.
    /// </summary>
    [Fact]
    public async Task OllamaProviderPropagatesCallerCancellation()
    {
        var handler = new StubHttpMessageHandler(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return JsonResponse(HttpStatusCode.OK, "{}");
            });
        var connector = CreateOllamaProvider(handler);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var action = () => connector.SearchAsync(
            "query",
            5,
            cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies the named client applies the reloadable timeout option.
    /// </summary>
    [Fact]
    public void NamedClientUsesConfiguredTimeout()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["WebSearch:Provider"] = WebSearchOptions.OllamaProvider,
                    ["WebSearch:Endpoint"] =
                        "https://ollama.com/api/web_search",
                    ["WebSearch:DefaultMaxResults"] = "5",
                    ["WebSearch:MaxResultsCap"] = "10",
                    ["WebSearch:TimeoutSeconds"] = "17",
                })
            .Build();
        var services = new ServiceCollection();
        services.AddConnectors(configuration);
        using var serviceProvider = services.BuildServiceProvider();

        using var client = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(ConnectorHttpClientNames.WebSearch);

        client.Timeout.Should().Be(TimeSpan.FromSeconds(17));
    }

    /// <summary>
    /// Verifies the facade reports a disabled provider as configuration.
    /// </summary>
    [Fact]
    public async Task FacadeRejectsDisabledWebSearch()
    {
        var provider = new StubWebSearchProvider(
            WebSearchOptions.OllamaProvider);
        var connector = new WebSearchConnector(
            [provider],
            new MutableOptionsMonitor<WebSearchOptions>(
                ValidOptions() with { Provider = string.Empty }));

        var action = () => connector.SearchAsync(
            "query",
            5,
            CancellationToken.None);

        await action.Should().ThrowAsync<WebSearchNotConfiguredException>()
            .WithMessage("*Tools page*");
        provider.CallCount.Should().Be(0);
    }

    /// <summary>
    /// Verifies unknown provider names cannot bypass the provider boundary.
    /// </summary>
    [Fact]
    public async Task FacadeRejectsUnknownProvider()
    {
        var provider = new StubWebSearchProvider(
            WebSearchOptions.OllamaProvider);
        var connector = new WebSearchConnector(
            [provider],
            new MutableOptionsMonitor<WebSearchOptions>(
                ValidOptions() with { Provider = "FutureProvider" }));

        var action = () => connector.SearchAsync(
            "query",
            5,
            CancellationToken.None);

        await action.Should().ThrowAsync<ConnectorConfigurationException>()
            .WithMessage("*FutureProvider*not registered*");
        provider.CallCount.Should().Be(0);
    }

    /// <summary>
    /// Verifies provider selection observes monitored configuration changes.
    /// </summary>
    [Fact]
    public async Task FacadeSelectsProviderAfterHotConfigurationChange()
    {
        var provider = new StubWebSearchProvider(
            WebSearchOptions.OllamaProvider);
        var options = new MutableOptionsMonitor<WebSearchOptions>(
            ValidOptions() with { Provider = string.Empty });
        var connector = new WebSearchConnector([provider], options);
        var disabledAction = () => connector.SearchAsync(
            "query",
            5,
            CancellationToken.None);
        await disabledAction.Should()
            .ThrowAsync<WebSearchNotConfiguredException>();

        options.Set(ValidOptions());
        var result = await connector.SearchAsync(
            "query",
            5,
            CancellationToken.None);

        result.Provider.Should().Be(WebSearchOptions.OllamaProvider);
        provider.CallCount.Should().Be(1);
    }

    private static OllamaWebSearchConnector CreateOllamaProvider(
        HttpMessageHandler handler,
        TimeSpan? timeout = null) =>
        new(
            new StubHttpClientFactory(handler, timeout),
            new MutableOptionsMonitor<WebSearchOptions>(ValidOptions()));

    private static WebSearchOptions ValidOptions() =>
        new()
        {
            Provider = WebSearchOptions.OllamaProvider,
            Endpoint = "https://ollama.com/api/web_search",
            ApiKey = "test-secret",
            DefaultMaxResults = 5,
            MaxResultsCap = 10,
            TimeoutSeconds = 1,
        };

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string content) =>
        new(statusCode)
        {
            Content = new StringContent(
                content,
                Encoding.UTF8,
                "application/json"),
        };

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private sealed class StubWebSearchProvider(string providerName)
        : IWebSearchProvider
    {
        public string ProviderName { get; } = providerName;

        public int CallCount { get; private set; }

        public Task<WebSearchResult> SearchAsync(
            string query,
            int maxResults,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(
                new WebSearchResult([], ProviderName));
        }
    }
}
