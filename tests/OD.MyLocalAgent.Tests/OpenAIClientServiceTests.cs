using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using System.Net;
using System.Text;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies OpenAI-compatible health checks against provider model catalogs.
/// </summary>
public sealed class OpenAIClientServiceTests
{
    private const string Model = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free";

    /// <summary>
    /// Verifies that an OpenAI-compatible health check lists models instead of treating a
    /// slash-containing model identifier as one encoded URL path segment.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsyncUsesModelCatalogAndFindsConfiguredModel()
    {
        HttpRequestMessage? receivedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            receivedRequest = request;
            return Task.FromResult(JsonResponse($"{{\"data\":[{{\"id\":\"{Model}\"}}]}}"));
        });
        var service = CreateService(handler);

        var health = await service.CheckHealthAsync(CancellationToken.None);

        health.IsReachable.Should().BeTrue();
        health.ModelAvailable.Should().BeTrue();
        receivedRequest.Should().NotBeNull();
        receivedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/models");
        receivedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        receivedRequest.Headers.Authorization.Parameter.Should().Be("unit-test-key");
    }

    /// <summary>
    /// Verifies that a reachable provider reports a missing configured model clearly.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsyncReportsUnavailableWhenCatalogDoesNotContainConfiguredModel()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse("{\"data\":[{\"id\":\"another-model\"}]}")));
        var service = CreateService(handler);

        var health = await service.CheckHealthAsync(CancellationToken.None);

        health.IsReachable.Should().BeTrue();
        health.ModelAvailable.Should().BeFalse();
        health.Error.Should().Be("The OpenAI-compatible endpoint did not list model 'nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free'.");
    }

    /// <summary>
    /// Verifies model catalog loading returns distinct identifiers in display order.
    /// </summary>
    [Fact]
    public async Task ListModelsAsyncReturnsDistinctSortedModelIdentifiers()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                JsonResponse(
                    "{\"data\":[{\"id\":\"zeta/model\"},{\"id\":\"alpha/model\"},{\"id\":\"ALPHA/MODEL\"}]}")));
        var service = CreateService(handler);

        var models = await service.ListModelsAsync(
            "https://openrouter.ai/api/v1",
            10,
            CancellationToken.None);

        models.Should().Equal("alpha/model", "zeta/model");
    }

    private static OpenAIClientService CreateService(HttpMessageHandler handler)
    {
        var options = new MutableOptionsMonitor<OpenAIOptions>(new OpenAIOptions
        {
            Endpoint = "https://openrouter.ai/api/v1",
            Model = Model,
            ApiKey = "unit-test-key",
        });
        return new OpenAIClientService(
            new StubHttpClientFactory(handler),
            options,
            TimeProvider.System,
            NullLogger<OpenAIClientService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
