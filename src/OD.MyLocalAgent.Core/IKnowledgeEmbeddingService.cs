namespace OD.MyLocalAgent.Core;

/// <summary>Generates validated normalized embeddings independently from chat configuration.</summary>
public interface IKnowledgeEmbeddingService
{
    /// <summary>Generates one normalized vector for each input in declaration order.</summary>
    Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);
}
