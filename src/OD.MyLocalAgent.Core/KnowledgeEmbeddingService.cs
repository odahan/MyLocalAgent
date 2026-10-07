using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>Generates and validates embeddings through Microsoft.Extensions.AI.</summary>
public sealed class KnowledgeEmbeddingService : IKnowledgeEmbeddingService
{
    private readonly IOptionsMonitor<KnowledgeOptions> _knowledgeOptions;
    private readonly IKnowledgeEmbeddingGeneratorFactory _generatorFactory;
    private readonly IOptionsMonitor<DecisionOptions>? _decisionOptions;
    private readonly IOptionsMonitor<GmailDecisionOptions>? _gmailDecisionOptions;
    private readonly IOptionsMonitor<OllamaOptions>? _ollamaOptions;
    private readonly ILocalDecisionClient? _decisionClient;
    private readonly DecisionModelResidencyCoordinator? _residency;
    private readonly GlobalLlmExecutionGate? _gate;

    /// <summary>Initializes the embedding service.</summary>
    public KnowledgeEmbeddingService(
        IOptionsMonitor<KnowledgeOptions> knowledgeOptions,
        IKnowledgeEmbeddingGeneratorFactory generatorFactory,
        IOptionsMonitor<DecisionOptions>? decisionOptions = null,
        IOptionsMonitor<OllamaOptions>? ollamaOptions = null,
        ILocalDecisionClient? decisionClient = null,
        DecisionModelResidencyCoordinator? residency = null,
        GlobalLlmExecutionGate? gate = null,
        IOptionsMonitor<GmailDecisionOptions>? gmailDecisionOptions = null)
    {
        _knowledgeOptions = knowledgeOptions;
        _generatorFactory = generatorFactory;
        _decisionOptions = decisionOptions;
        _gmailDecisionOptions = gmailDecisionOptions;
        _ollamaOptions = ollamaOptions;
        _decisionClient = decisionClient;
        _residency = residency;
        _gate = gate;
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
        if (_residency is not null && _ollamaOptions is not null && options.EmbeddingProvider == "Ollama")
        {
            var local = _ollamaOptions.CurrentValue;
            return await _residency.EmbeddingAsync(local.Endpoint, async () =>
            {
                var decision = _decisionOptions?.CurrentValue;
                var mail = _gmailDecisionOptions?.CurrentValue ?? new GmailDecisionOptions();
                if (decision is not null && (decision.Enabled || mail.Enabled) && _decisionClient is { } client &&
                    DecisionOptions.IsValid(decision) && LocalDecisionEndpoint.IsValid(local.Endpoint) &&
                    DecisionModelIdentity.SameEndpoint(decision.DecisionEndpoint ?? local.Endpoint, local.Endpoint))
                {
                    var identities = await client.GetModelIdentitiesAsync(local.Endpoint, timeout.Token).ConfigureAwait(false);
                    if (DecisionModelIdentity.TryGet(identities, decision.DecisionModel, out var dedicated) &&
                        new[] { local.Model, mail.ArbitrationModel ?? local.Model,
                            mail.FallbackModel ?? mail.ArbitrationModel ?? local.Model, options.EmbeddingModel }
                        .All(model => !DecisionModelIdentity.TryGet(identities, model, out var identity) || identity != dedicated))
                        await client.ReleaseForEmbeddingAsync(local.Endpoint, decision.DecisionModel, timeout.Token).ConfigureAwait(false);
                }
                using var gate = _gate is null ? null : await _gate.EnterAsync(timeout.Token).ConfigureAwait(false);
                return await GenerateCoreAsync(inputs, options, timeout.Token, cancellationToken).ConfigureAwait(false);
            }, timeout.Token).ConfigureAwait(false);
        }
        return await GenerateCoreAsync(inputs, options, timeout.Token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<float[]>> GenerateCoreAsync(IReadOnlyList<string> inputs, KnowledgeOptions options, CancellationToken cancellationToken, CancellationToken callerToken)
    {
        using var generator = _generatorFactory.Create(options);
        var output = new List<float[]>(inputs.Count);
        try
        {
            for (var offset = 0; offset < inputs.Count; offset += options.EmbeddingBatchSize)
            {
                var batch = inputs.Skip(offset).Take(options.EmbeddingBatchSize).ToArray();
                var generated = await generator.GenerateAsync(batch, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
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
