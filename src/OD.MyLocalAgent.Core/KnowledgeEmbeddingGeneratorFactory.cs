using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Ollama;
using OllamaSharp;
using OpenAI;
using OpenAI.Embeddings;

namespace OD.MyLocalAgent.Core;

/// <summary>Creates Ollama or OpenAI-compatible embedding generators.</summary>
public sealed class KnowledgeEmbeddingGeneratorFactory : IKnowledgeEmbeddingGeneratorFactory
{
    private readonly IOptionsMonitor<OllamaOptions> _ollamaOptions;
    private readonly IOptionsMonitor<OpenAIOptions> _openAiOptions;

    /// <summary>Initializes the Knowledge embedding generator factory.</summary>
    public KnowledgeEmbeddingGeneratorFactory(
        IOptionsMonitor<OllamaOptions> ollamaOptions,
        IOptionsMonitor<OpenAIOptions> openAiOptions)
    {
        _ollamaOptions = ollamaOptions;
        _openAiOptions = openAiOptions;
    }

    /// <inheritdoc />
    public IEmbeddingGenerator<string, Embedding<float>> Create(KnowledgeOptions options)
    {
        if (string.Equals(options.EmbeddingProvider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return new OllamaApiClient(
                new Uri(_ollamaOptions.CurrentValue.Endpoint),
                options.EmbeddingModel);
        }

        if (string.Equals(options.EmbeddingProvider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var provider = _openAiOptions.CurrentValue;
            if (string.IsNullOrWhiteSpace(provider.ApiKey))
            {
                throw new InvalidOperationException(
                    "The OpenAI embedding provider requires an API key in the secret store.");
            }

            var client = new EmbeddingClient(
                options.EmbeddingModel,
                new ApiKeyCredential(provider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(provider.Endpoint) });
            return client.AsIEmbeddingGenerator();
        }

        throw new InvalidOperationException(
            $"Unknown Knowledge embedding provider '{options.EmbeddingProvider}'.");
    }
}
