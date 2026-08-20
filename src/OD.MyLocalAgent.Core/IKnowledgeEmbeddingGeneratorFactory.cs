using Microsoft.Extensions.AI;

namespace OD.MyLocalAgent.Core;

/// <summary>Creates the configured provider-neutral Knowledge embedding generator.</summary>
public interface IKnowledgeEmbeddingGeneratorFactory
{
    /// <summary>Creates one generator for a validated Knowledge configuration snapshot.</summary>
    IEmbeddingGenerator<string, Embedding<float>> Create(KnowledgeOptions options);
}
