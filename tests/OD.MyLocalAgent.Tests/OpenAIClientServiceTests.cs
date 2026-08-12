using FluentAssertions;
using Microsoft.Extensions.AI;
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

    /// <summary>Verifies only provider-confirmed successful streaming completions are accepted.</summary>
    [Theory]
    [InlineData("stop", false)]
    [InlineData("tool_calls", false)]
    [InlineData("length", true)]
    [InlineData("content_filter", true)]
    public void StreamingCompletionValidatorRejectsIncompleteFinishes(
        string finishReason,
        bool shouldThrow)
    {
        var action = () => OpenAIStreamingCompletionValidator.EnsureCompleted(
            new ChatFinishReason(finishReason),
            updateCount: 8,
            textCharacterCount: 120);

        if (shouldThrow)
        {
            action.Should().Throw<OpenAIStreamingCompletionException>()
                .WithMessage("*reason: " + finishReason + "*");
        }
        else
        {
            action.Should().NotThrow();
        }
    }

    /// <summary>Verifies a stream with no completion marker is rejected with diagnostics.</summary>
    [Fact]
    public void StreamingCompletionValidatorRejectsMissingFinish()
    {
        var action = () => OpenAIStreamingCompletionValidator.EnsureCompleted(
            null,
            updateCount: 3,
            textCharacterCount: 42);

        action.Should().Throw<OpenAIStreamingCompletionException>()
            .WithMessage("*reason: missing; updates: 3; text characters: 42*");
    }

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

    /// <summary>Verifies compatibility never enables media capabilities implicitly.</summary>
    [Fact]
    public void CompatibleEndpointDefaultsToTextOnly()
    {
        var service = CreateService(new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse("{}"))));

        service.Capabilities.Should().Be(ProviderCapabilities.TextChat);
    }

    /// <summary>Verifies image generation uses its dedicated endpoint and decodes returned bytes.</summary>
    [Fact]
    public async Task GenerateImageUsesDedicatedImageEndpoint()
    {
        HttpRequestMessage? received = null;
        string? receivedBody = null;
        var expected = new byte[] { 1, 2, 3, 4 };
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            received = request;
            receivedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                $"{{\"data\":[{{\"b64_json\":\"{Convert.ToBase64String(expected)}\"}}]}}");
        });
        var service = CreateService(handler, new OpenAIOptions
        {
            Endpoint = "https://api.openai.com/v1",
            Model = Model,
            ApiKey = "unit-test-key",
            SupportsImageOutput = true,
            ImageModel = "gpt-image-test",
        });

        var result = await service.GenerateMediaAsync(
            new MultimodalGenerationRequest { Kind = MultimodalGenerationKind.Image, Prompt = "A safe test image" },
            CancellationToken.None);

        received!.RequestUri!.AbsolutePath.Should().Be("/v1/images/generations");
        receivedBody.Should().Contain("gpt-image-test");
        result.MediaType.Should().Be("image/png");
        result.Data.Should().Equal(expected);
    }

    /// <summary>Verifies speech generation uses its dedicated endpoint and retains WAV output.</summary>
    [Fact]
    public async Task GenerateAudioUsesDedicatedSpeechEndpoint()
    {
        HttpRequestMessage? received = null;
        string? receivedBody = null;
        var wave = new byte[48];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wave, 0);
        BitConverter.GetBytes(8_000).CopyTo(wave, 28);
        BitConverter.GetBytes(8_000).CopyTo(wave, 40);
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            received = request;
            receivedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(wave),
            };
        });
        var service = CreateService(handler, new OpenAIOptions
        {
            Endpoint = "https://api.openai.com/v1",
            Model = Model,
            ApiKey = "unit-test-key",
            SupportsAudioOutput = true,
            AudioModel = "tts-test",
            AudioVoice = "coral",
        });

        var result = await service.GenerateMediaAsync(
            new MultimodalGenerationRequest { Kind = MultimodalGenerationKind.Audio, Prompt = "Read a safe test" },
            CancellationToken.None);

        received!.RequestUri!.AbsolutePath.Should().Be("/v1/audio/speech");
        receivedBody.Should().Contain("tts-test").And.Contain("response_format").And.Contain("wav");
        result.MediaType.Should().Be("audio/wav");
        result.Duration.Should().Be(TimeSpan.FromSeconds(1));
    }

    private static OpenAIClientService CreateService(
        HttpMessageHandler handler,
        OpenAIOptions? configuredOptions = null)
    {
        var options = new MutableOptionsMonitor<OpenAIOptions>(configuredOptions ?? new OpenAIOptions
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
