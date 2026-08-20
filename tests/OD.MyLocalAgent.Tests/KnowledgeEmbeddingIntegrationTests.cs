using FluentAssertions;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Exercises the configured local Ollama Knowledge embedding endpoint.</summary>
[Trait("Category", "Integration")]
public sealed class KnowledgeEmbeddingIntegrationTests
{
    /// <summary>Verifies the pinned multilingual model embeds French and English consistently.</summary>
    [Fact]
    public async Task EmbeddingGemmaEmbedsFrenchAndEnglish()
    {
        var knowledge = new KnowledgeOptions
        {
            EmbeddingProvider = "Ollama",
            EmbeddingModel = "embeddinggemma:300m",
            EmbeddingBatchSize = 2,
            EmbeddingTimeoutSeconds = 120,
        };
        var factory = new KnowledgeEmbeddingGeneratorFactory(
            new MutableOptionsMonitor<OllamaOptions>(new OllamaOptions()),
            new MutableOptionsMonitor<OpenAIOptions>(new OpenAIOptions()));
        var service = new KnowledgeEmbeddingService(
            new MutableOptionsMonitor<KnowledgeOptions>(knowledge),
            factory);

        var vectors = await service.GenerateAsync(
            [
                "Comment gérer les erreurs au démarrage de l'application ?",
                "How should application startup failures be handled?",
            ],
            CancellationToken.None);

        vectors.Should().HaveCount(2);
        vectors[0].Length.Should().BeGreaterThan(0);
        vectors[1].Length.Should().Be(vectors[0].Length);
        vectors.Should().OnlyContain(vector =>
            vector.All(float.IsFinite) &&
            Math.Abs(Math.Sqrt(vector.Sum(value => (double)value * value)) - 1d) < 0.0001d);
    }
}
