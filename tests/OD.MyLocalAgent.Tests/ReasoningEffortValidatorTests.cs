using System.Net;
using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies provider-aware reasoning-effort probes and stable-result caching.</summary>
public sealed class ReasoningEffortValidatorTests
{
    /// <summary>Verifies save-time validation never queues a request on a busy provider.</summary>
    [Fact]
    public void CachedValidationDoesNotContactProvider()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            calls++;
            throw new InvalidOperationException("The provider is busy.");
        });
        var validator = CreateValidator(handler, AIOptions.OllamaProvider);

        var result = validator.ValidateCached("model", "high");

        result.Status.Should().Be(ReasoningEffortValidationStatus.Unavailable);
        result.Message.Should().Contain("has not been verified");
        validator.ValidateCached("model", " high ").Status.Should().Be(ReasoningEffortValidationStatus.Rejected);
        calls.Should().Be(0);
    }

    /// <summary>Verifies save-time validation preserves cached results for the exact model and effort.</summary>
    [Theory]
    [InlineData(HttpStatusCode.OK, ReasoningEffortValidationStatus.Accepted)]
    [InlineData(HttpStatusCode.BadRequest, ReasoningEffortValidationStatus.Rejected)]
    public async Task CachedValidationUsesKnownProviderResult(
        HttpStatusCode status,
        ReasoningEffortValidationStatus expected)
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("reasoning effort diagnostic"),
            });
        });
        var validator = CreateValidator(handler, AIOptions.OllamaProvider);
        await validator.ValidateAsync("model", "high", CancellationToken.None);

        validator.ValidateCached("model", "high").Status.Should().Be(expected);
        validator.ValidateCached("other-model", "high").Status.Should().Be(ReasoningEffortValidationStatus.Unavailable);
        validator.ValidateCached("model", "low").Status.Should().Be(ReasoningEffortValidationStatus.Unavailable);
        calls.Should().Be(1);
    }

    /// <summary>Verifies Ollama receives its native string-valued think parameter.</summary>
    [Fact]
    public async Task OllamaProbeSendsNativeThinkValueAndCachesAcceptance()
    {
        var calls = 0;
        string? body = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            calls++;
            request.RequestUri!.AbsolutePath.Should().Be("/api/chat");
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var validator = CreateValidator(handler, AIOptions.OllamaProvider);

        var first = await validator.ValidateAsync("gemma4:12b", "xhigh", CancellationToken.None);
        var second = await validator.ValidateAsync("gemma4:12b", "xhigh", CancellationToken.None);

        first.Status.Should().Be(ReasoningEffortValidationStatus.Accepted);
        second.Status.Should().Be(ReasoningEffortValidationStatus.Accepted);
        calls.Should().Be(1);
        using var document = JsonDocument.Parse(body!);
        document.RootElement.GetProperty("think").GetString().Should().Be("xhigh");
    }

    /// <summary>Verifies OpenAI-compatible endpoints receive the exact user keyword.</summary>
    [Fact]
    public async Task OpenAIProbeSendsExactEffortAndRecognizesRejection()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/v1/chat/completions");
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("reasoning_effort is not supported by this model"),
            };
        });
        var validator = CreateValidator(handler, AIOptions.OpenAIProvider);

        var result = await validator.ValidateAsync("compatible-model", "max", CancellationToken.None);

        result.Status.Should().Be(ReasoningEffortValidationStatus.Rejected);
        using var document = JsonDocument.Parse(body!);
        document.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("max");
    }

    /// <summary>Verifies inconclusive provider failures are retried instead of cached.</summary>
    [Fact]
    public async Task UnavailableProbeIsNotCached()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        var validator = CreateValidator(handler, AIOptions.OllamaProvider);

        var first = await validator.ValidateAsync("model", "high", CancellationToken.None);
        var second = await validator.ValidateAsync("model", "high", CancellationToken.None);

        first.Status.Should().Be(ReasoningEffortValidationStatus.Unavailable);
        second.Status.Should().Be(ReasoningEffortValidationStatus.Unavailable);
        calls.Should().Be(2);
    }

    private static ReasoningEffortValidator CreateValidator(
        HttpMessageHandler handler,
        string provider) =>
        new(
            new StubHttpClientFactory(handler),
            new MutableOptionsMonitor<AIOptions>(new AIOptions { Provider = provider }),
            new MutableOptionsMonitor<OllamaOptions>(new OllamaOptions
            {
                Endpoint = "http://localhost:11434",
                Model = "gemma4:12b",
                TimeoutSeconds = 10,
            }),
            new MutableOptionsMonitor<OpenAIOptions>(new OpenAIOptions
            {
                Endpoint = "https://compatible.example/v1",
                Model = "compatible-model",
                ApiKey = "test-key",
                TimeoutSeconds = 10,
            }));
}
