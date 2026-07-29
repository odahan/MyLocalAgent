using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies provider selection and actionable language-model configuration failures.
/// </summary>
public sealed class LlmClientFactoryTests
{
    /// <summary>
    /// Verifies that Ollama is selected by default.
    /// </summary>
    [Fact]
    public void CreateSelectsOllamaByDefault()
    {
        var context = CreateFactoryContext(new AIOptions());

        var service = context.Factory.Create();

        service.Should().BeSameAs(context.Ollama);
        service.ProviderName.Should().Be(AIOptions.OllamaProvider);
    }

    /// <summary>
    /// Verifies that provider changes are observed on each factory call.
    /// </summary>
    [Fact]
    public void CreateObservesHotProviderSwitch()
    {
        var context = CreateFactoryContext(new AIOptions());
        context.Factory.Create().Should().BeSameAs(context.Ollama);

        context.AIOptions.Set(new AIOptions { Provider = AIOptions.OpenAIProvider });
        var service = context.Factory.Create();

        service.Should().BeSameAs(context.OpenAI);
        service.ProviderName.Should().Be(AIOptions.OpenAIProvider);
    }

    /// <summary>
    /// Verifies that unknown providers produce an actionable typed error.
    /// </summary>
    [Fact]
    public void CreateRejectsUnknownProvider()
    {
        var context = CreateFactoryContext(new AIOptions { Provider = "Unexpected" });

        var action = context.Factory.Create;

        action.Should().Throw<LlmConfigurationException>()
            .WithMessage("*Unexpected*Ollama*OpenAI*");
    }

    /// <summary>
    /// Verifies that selecting OpenAI without an API key produces a typed error.
    /// </summary>
    [Fact]
    public void CreateRejectsOpenAIWithoutApiKey()
    {
        var context = CreateFactoryContext(
            new AIOptions { Provider = AIOptions.OpenAIProvider },
            new OpenAIOptions { ApiKey = null });

        var action = context.Factory.Create;

        action.Should().Throw<LlmConfigurationException>()
            .WithMessage("*OpenAI:ApiKey*");
    }

    /// <summary>
    /// Verifies that invalid Ollama settings produce a typed error.
    /// </summary>
    [Fact]
    public void CreateRejectsInvalidOllamaEndpoint()
    {
        var context = CreateFactoryContext(
            new AIOptions(),
            ollamaOptions: new OllamaOptions { Endpoint = "not-a-uri" });

        var action = context.Factory.Create;

        action.Should().Throw<LlmConfigurationException>()
            .WithMessage("*Ollama:Endpoint*");
    }

    private static FactoryContext CreateFactoryContext(
        AIOptions aiOptions,
        OpenAIOptions? openAIOptions = null,
        OllamaOptions? ollamaOptions = null)
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("No HTTP call is expected."));
        var httpClientFactory = new StubHttpClientFactory(handler);
        var aiMonitor = new MutableOptionsMonitor<AIOptions>(aiOptions);
        var ollamaMonitor = new MutableOptionsMonitor<OllamaOptions>(
            ollamaOptions ?? new OllamaOptions());
        var openAIMonitor = new MutableOptionsMonitor<OpenAIOptions>(
            openAIOptions ?? new OpenAIOptions { ApiKey = "unit-test-key" });
        var ollama = new OllamaClientService(
            httpClientFactory,
            ollamaMonitor,
            TimeProvider.System,
            NullLogger<OllamaClientService>.Instance);
        var openAI = new OpenAIClientService(
            httpClientFactory,
            openAIMonitor,
            TimeProvider.System,
            NullLogger<OpenAIClientService>.Instance);
        var factory = new LlmClientFactory(
            aiMonitor,
            ollamaMonitor,
            openAIMonitor,
            ollama,
            openAI);
        return new FactoryContext(factory, aiMonitor, ollama, openAI);
    }

    private sealed record FactoryContext(
        LlmClientFactory Factory,
        MutableOptionsMonitor<AIOptions> AIOptions,
        OllamaClientService Ollama,
        OpenAIClientService OpenAI);
}
