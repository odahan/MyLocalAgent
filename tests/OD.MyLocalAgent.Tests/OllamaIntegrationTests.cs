using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Provides an opt-in local Ollama connectivity probe.
/// </summary>
public sealed class OllamaIntegrationTests
{
    /// <summary>
    /// Probes the default local Ollama endpoint and configured model when explicitly enabled.
    /// </summary>
    [Fact(Skip = "Manual probe: requires Ollama and gemma4:12b on localhost:11434.")]
    public async Task LocalOllamaReportsReachableConfiguredModel()
    {
        using var handler = new HttpClientHandler();
        var service = new OllamaClientService(
            new StubHttpClientFactory(handler),
            new MutableOptionsMonitor<OllamaOptions>(new OllamaOptions()),
            TimeProvider.System,
            NullLogger<OllamaClientService>.Instance);

        var health = await service.CheckHealthAsync(CancellationToken.None);

        health.IsReachable.Should().BeTrue(health.Error);
        health.ModelAvailable.Should().BeTrue(health.Error);
    }
}
