using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the Tavily HTTP contract without using API credits.</summary>
public sealed class TavilyWebSearchConnectorTests
{
    /// <summary>Verifies Basic search, independent authentication and normalized results.</summary>
    [Fact]
    public async Task SearchUsesBasicModeAndTavilyCredentials()
    {
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be(new Uri("https://api.tavily.com/search"));
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            request.Headers.Authorization.Parameter.Should().Be("tavily-secret");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            body.RootElement.GetProperty("query").GetString().Should().Be("test query");
            body.RootElement.GetProperty("max_results").GetInt32().Should().Be(1);
            body.RootElement.GetProperty("search_depth").GetString().Should().Be("basic");
            body.RootElement.GetProperty("auto_parameters").GetBoolean().Should().BeFalse();
            body.RootElement.GetProperty("include_answer").GetBoolean().Should().BeFalse();
            return Response(200, """{"results":[{"title":"Source","url":"https://example.com","content":"Excerpt","score":0.9},{"title":"Extra"}],"usage":{"credits":1}}""");
        });
        var result = await Create(handler).SearchAsync("test query", 1, CancellationToken.None);
        result.Provider.Should().Be("Tavily");
        result.Items.Should().ContainSingle().Which.Should()
            .Be(new WebSearchResultItem("Source", "https://example.com", "Excerpt"));
    }

    /// <summary>Verifies the facade can select and hot-switch both registered providers.</summary>
    [Fact]
    public async Task RegisteredProvidersUseIndependentKeysAfterSwitching()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebSearch:Provider"] = "Tavily",
            ["WebSearch:TavilyApiKey"] = "tavily-secret",
            ["WebSearch:ApiKey"] = "ollama-secret",
        }).Build();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            request.Headers.Authorization!.Parameter.Should().Be(
                request.RequestUri!.Host == "api.tavily.com" ? "tavily-secret" : "ollama-secret");
            return Task.FromResult(Response(200, """{"results":[]}"""));
        });
        var services = new ServiceCollection();
        services.AddConnectors(configuration);
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(handler));
        using var provider = services.BuildServiceProvider();
        var connector = provider.GetRequiredService<IWebSearchConnector>();
        (await connector.SearchAsync("query", 1, CancellationToken.None)).Provider.Should().Be("Tavily");
        configuration["WebSearch:Provider"] = "Ollama";
        configuration.Reload();
        (await connector.SearchAsync("query", 1, CancellationToken.None)).Provider.Should().Be("Ollama");
    }

    /// <summary>Verifies provider error bodies cannot expose keys in diagnostics.</summary>
    [Theory]
    [InlineData(401, "API key")]
    [InlineData(429, "rate or credit limit")]
    [InlineData(432, "rate or credit limit")]
    [InlineData(433, "rate or credit limit")]
    [InlineData(500, "HTTP 500")]
    public async Task HttpErrorsHaveTypedSafeDiagnostics(int status, string message)
    {
        var connector = Create(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(Response(status, "tavily-secret"))));
        var action = () => connector.SearchAsync("query", 1, CancellationToken.None);
        var failure = (await action.Should().ThrowAsync<WebSearchApiException>()).Which;
        failure.StatusCode.Should().Be(status);
        failure.Message.Should().Contain(message).And.NotContain("tavily-secret");
    }

    /// <summary>Verifies the Ollama key cannot substitute for a missing Tavily key.</summary>
    [Fact]
    public async Task MissingTavilyKeyFailsBeforeSending()
    {
        var connector = Create(new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Must not send")), key: null);
        var action = () => connector.SearchAsync("query", 1, CancellationToken.None);
        await action.Should().ThrowAsync<ConnectorConfigurationException>().WithMessage("*TavilyApiKey*");
    }

    /// <summary>Verifies malformed JSON and timeout are typed, while caller cancellation propagates.</summary>
    [Fact]
    public async Task MalformedJsonAndCancellationAreDistinguished()
    {
        var malformed = Create(new StubHttpMessageHandler((_, _) => Task.FromResult(Response(200, "{"))));
        var action = () => malformed.SearchAsync("query", 1, CancellationToken.None);
        await action.Should().ThrowAsync<WebSearchApiException>().WithMessage("*malformed JSON*");
        var canceled = Create(new StubHttpMessageHandler((_, _) => throw new OperationCanceledException()));
        var timeout = () => canceled.SearchAsync("query", 1, CancellationToken.None);
        await timeout.Should().ThrowAsync<WebSearchApiException>().WithMessage("*timed out*");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var caller = () => canceled.SearchAsync("query", 1, cancellation.Token);
        await caller.Should().ThrowAsync<OperationCanceledException>();
    }

    private static TavilyWebSearchConnector Create(HttpMessageHandler handler, string? key = "tavily-secret") =>
        new(new StubHttpClientFactory(handler), new MutableOptionsMonitor<WebSearchOptions>(new()
        {
            Provider = "Tavily",
            TavilyApiKey = key,
            ApiKey = "ollama-secret",
        }));

    private static HttpResponseMessage Response(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body) };
}
