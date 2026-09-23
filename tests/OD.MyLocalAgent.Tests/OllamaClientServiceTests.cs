using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies Ollama transport, health, timeout, model validation, and streaming behavior.
/// </summary>
public sealed class OllamaClientServiceTests
{
    private const string Model = "gemma4:12b";

    /// <summary>
    /// Verifies successful health and non-streaming generation.
    /// </summary>
    [Fact]
    public async Task GenerateAsyncReturnsAssistantTextWhenEndpointAndModelAreAvailable()
    {
        var requestedPaths = new List<string>();
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath == "/api/tags")
            {
                return JsonResponse(CreateTagsJson(Model));
            }

            var body = await request.Content!.ReadAsStringAsync();
            body.Should().Contain($"\"model\":\"{Model}\"");
            return JsonResponse(CreateChatJson("Hello from Ollama."));
        });
        var service = CreateService(handler);

        var health = await service.CheckHealthAsync(CancellationToken.None);
        var response = await service.GenerateAsync("Say hello.", CancellationToken.None);

        health.IsReachable.Should().BeTrue();
        health.ModelAvailable.Should().BeTrue();
        response.Should().Be("Hello from Ollama.");
        requestedPaths.Should().ContainInOrder("/api/tags", "/api/tags", "/api/chat");
    }

    /// <summary>
    /// Verifies warm-up performs a real model generation with the task context policy.
    /// </summary>
    [Fact]
    public async Task WarmUpAsyncLoadsModelAndLimitsResponseToOneToken()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
            {
                return JsonResponse(CreateTagsJson(Model));
            }

            requestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(CreateChatJson("READY"));
        });
        var service = CreateService(handler);
        var policy = new EffectiveGenerationPolicy(
            0.1,
            1,
            32_768,
            GenerationOptionSource.Task,
            GenerationOptionSource.Settings);

        await service.WarmUpAsync(policy, CancellationToken.None);

        requestBody.Should().NotBeNull();
        requestBody.Should().Contain("Reply with READY only.");
        requestBody.Should().Contain("\"num_ctx\":32768");
        requestBody.Should().Contain("\"num_predict\":1");
    }

    /// <summary>
    /// Verifies that an unreachable endpoint produces health failure and a typed generation exception.
    /// </summary>
    [Fact]
    public async Task EndpointFailureReturnsUnhealthyAndThrowsTypedException()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new HttpRequestException("Connection refused."));
        var service = CreateService(handler);

        var health = await service.CheckHealthAsync(CancellationToken.None);
        var action = () => service.GenerateAsync("Hello.", CancellationToken.None);

        health.IsReachable.Should().BeFalse();
        health.ModelAvailable.Should().BeFalse();
        await action.Should().ThrowAsync<OllamaUnavailableException>()
            .WithMessage("*unavailable*");
    }

    /// <summary>
    /// Verifies that a missing configured model is reported and surfaced as a typed exception.
    /// </summary>
    [Fact]
    public async Task MissingModelReturnsUnhealthyModelAndThrowsTypedException()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse(CreateTagsJson("another-model:latest"))));
        var service = CreateService(handler);

        var health = await service.CheckHealthAsync(CancellationToken.None);
        var action = () => service.GenerateAsync("Hello.", CancellationToken.None);

        health.IsReachable.Should().BeTrue();
        health.ModelAvailable.Should().BeFalse();
        await action.Should().ThrowAsync<OllamaModelMissingException>()
            .Where(exception => exception.Model == Model);
    }

    /// <summary>
    /// Verifies that the configured request timeout becomes a typed availability error.
    /// </summary>
    [Fact]
    public async Task RequestTimeoutThrowsTypedUnavailableException()
    {
        var handler = new StubHttpMessageHandler((request, cancellationToken) =>
        {
            return request.RequestUri!.AbsolutePath == "/api/tags"
                ? Task.FromResult(JsonResponse(CreateTagsJson(Model)))
                : WaitUntilCancelledAsync(cancellationToken);
        });
        var service = CreateService(handler, timeoutSeconds: 1);

        var action = () => service.GenerateAsync("Wait.", CancellationToken.None);

        await action.Should().ThrowAsync<OllamaUnavailableException>()
            .WithMessage("*within 1 seconds*");
    }

    /// <summary>
    /// Verifies that all streamed response fragments can be concatenated in order.
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsyncYieldsConcatenableFragments()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
            {
                return Task.FromResult(JsonResponse(CreateTagsJson(Model)));
            }

            var content = string.Join(
                "\n",
                CreateChatJson("Hello", done: false),
                CreateChatJson(" world", done: false),
                CreateChatJson("!", done: true),
                string.Empty);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/x-ndjson"),
            });
        });
        var service = CreateService(handler);
        var builder = new StringBuilder();

        await foreach (var fragment in service.GenerateStreamingAsync(
            "Say hello.",
            CancellationToken.None))
        {
            builder.Append(fragment);
        }

        builder.ToString().Should().Be("Hello world!");
    }

    /// <summary>
    /// Verifies the model catalog returns distinct installed tags in display order.
    /// </summary>
    [Fact]
    public async Task ListInstalledModelsAsyncReturnsDistinctSortedModelTags()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                JsonResponse(
                    CreateTagsJson(
                        "zeta:latest",
                        "alpha:7b",
                        "ALPHA:7B"))));
        var service = CreateService(handler);

        var models = await service.ListInstalledModelsAsync(
            "http://localhost:11434",
            10,
            CancellationToken.None);

        models.Should().Equal("alpha:7b", "zeta:latest");
    }

    /// <summary>Verifies image support comes from the selected model's Ollama metadata.</summary>
    [Theory]
    [InlineData("completion", false)]
    [InlineData("completion,vision,thinking", true)]
    public async Task SupportsImageInputAsyncReadsModelCapabilities(string capabilities, bool expected)
    {
        string? requestedModel = null;
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/show");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            requestedModel = body.RootElement.GetProperty("model").GetString();
            return JsonResponse(JsonSerializer.Serialize(new
            {
                capabilities = capabilities.Split(','),
            }));
        });
        var service = CreateService(handler);

        var supported = await service.SupportsImageInputAsync(CancellationToken.None);

        supported.Should().Be(expected);
        requestedModel.Should().Be(Model);
    }

    private static OllamaClientService CreateService(
        HttpMessageHandler handler,
        int timeoutSeconds = 10)
    {
        var options = new MutableOptionsMonitor<OllamaOptions>(new OllamaOptions
        {
            Endpoint = "http://localhost:11434",
            Model = Model,
            TimeoutSeconds = timeoutSeconds,
        });
        return new OllamaClientService(
            new StubHttpClientFactory(handler),
            options,
            TimeProvider.System,
            NullLogger<OllamaClientService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static string CreateTagsJson(params string[] models)
    {
        return JsonSerializer.Serialize(new
        {
            models = models.Select(
                model => new
                {
                    name = model,
                    model,
                    modified_at = "2026-07-29T00:00:00Z",
                    size = 1,
                    digest = "abc",
                    details = new
                    {
                        format = "gguf",
                        family = "gemma",
                        families = new[] { "gemma" },
                        parameter_size = "12B",
                        quantization_level = "Q4",
                    },
                }),
        });
    }

    private static string CreateChatJson(string text, bool done = true)
    {
        return JsonSerializer.Serialize(new
        {
            model = Model,
            created_at = "2026-07-29T00:00:00Z",
            message = new { role = "assistant", content = text },
            done,
        });
    }

    private static async Task<HttpResponseMessage> WaitUntilCancelledAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The cancellation delay unexpectedly completed.");
    }
}
