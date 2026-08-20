using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>Generates and validates embeddings through Microsoft.Extensions.AI.</summary>
public sealed class KnowledgeEmbeddingService : IKnowledgeEmbeddingService
{
    private readonly IOptionsMonitor<KnowledgeOptions> _knowledgeOptions;
    private readonly IKnowledgeEmbeddingGeneratorFactory _generatorFactory;

    /// <summary>Initializes the embedding service.</summary>
    public KnowledgeEmbeddingService(
        IOptionsMonitor<KnowledgeOptions> knowledgeOptions,
        IKnowledgeEmbeddingGeneratorFactory generatorFactory)
    {
        _knowledgeOptions = knowledgeOptions;
        _generatorFactory = generatorFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> GenerateAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            return [];
        }

        var options = _knowledgeOptions.CurrentValue;
        if (!KnowledgeOptions.IsValid(options))
        {
            throw new InvalidOperationException("Knowledge embedding settings are invalid.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.EmbeddingTimeoutSeconds));
        using var generator = _generatorFactory.Create(options);
        var output = new List<float[]>(inputs.Count);
        try
        {
            for (var offset = 0; offset < inputs.Count; offset += options.EmbeddingBatchSize)
            {
                var batch = inputs.Skip(offset).Take(options.EmbeddingBatchSize).ToArray();
                var generated = await generator.GenerateAsync(batch, cancellationToken: timeout.Token).ConfigureAwait(false);
                if (generated.Count != batch.Length)
                {
                    throw new InvalidOperationException("The embedding provider returned an unexpected result count.");
                }

                foreach (var embedding in generated)
                {
                    var vector = embedding.Vector.ToArray();
                    Normalize(vector);
                    output.Add(vector);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The Knowledge embedding provider did not respond within {options.EmbeddingTimeoutSeconds} seconds.");
        }

        var dimension = output[0].Length;
        if (output.Any(vector => vector.Length != dimension))
        {
            throw new InvalidOperationException("The embedding provider returned inconsistent dimensions.");
        }

        return output;
    }

    private static void Normalize(float[] vector)
    {
        if (vector.Length == 0 || vector.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidOperationException("The embedding provider returned an invalid vector.");
        }

        var magnitude = Math.Sqrt(vector.Sum(value => (double)value * value));
        if (magnitude <= 0 || !double.IsFinite(magnitude))
        {
            throw new InvalidOperationException("The embedding provider returned a zero vector.");
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / magnitude);
        }
    }
}
