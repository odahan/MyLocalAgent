using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Applies one immutable generation policy to every request in an execution.
/// </summary>
public sealed class GenerationPolicyChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly bool _mapsOllamaContext;
    private readonly string _operation;

    /// <summary>
    /// Initializes a new policy-bound chat client.
    /// </summary>
    /// <param name="inner">The shared provider chat client.</param>
    /// <param name="policy">The immutable effective policy.</param>
    /// <param name="providerName">The selected provider name.</param>
    /// <param name="operation">The logical operation that initiated model calls.</param>
    public GenerationPolicyChatClient(
        IChatClient inner,
        EffectiveGenerationPolicy policy,
        string providerName,
        string operation = "llm")
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        _inner = inner;
        Policy = policy;
        _mapsOllamaContext = string.Equals(
            providerName,
            AIOptions.OllamaProvider,
            StringComparison.OrdinalIgnoreCase);
        _operation = operation;
    }

    /// <summary>
    /// Gets the immutable policy applied by this wrapper.
    /// </summary>
    public EffectiveGenerationPolicy Policy { get; }

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _inner.GetResponseAsync(
            messages,
            CreateOptions(options),
            cancellationToken).ConfigureAwait(false);
        TaskTimingCollector.Current.Value?.RecordLlmUsage(_operation, response.Usage);
        return response;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        UsageDetails? usage = null;
        try
        {
            await foreach (var update in _inner
                .GetStreamingResponseAsync(
                    messages,
                    CreateOptions(options),
                    cancellationToken)
                .ConfigureAwait(false))
            {
                usage = update.Contents.OfType<UsageContent>()
                    .LastOrDefault()?.Details ?? usage;
                yield return update;
            }
        }
        finally
        {
            TaskTimingCollector.Current.Value?.RecordLlmUsage(_operation, usage);
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : _inner.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The provider client is shared and owned by dependency injection.
    }

    private ChatOptions CreateOptions(ChatOptions? options)
    {
        var effective = options?.Clone() ?? new ChatOptions();
        effective.Temperature = (float)Policy.Temperature;
        effective.TopP = (float)Policy.TopP;
        if (_mapsOllamaContext)
        {
            effective.AddOllamaOption(
                OllamaOption.NumCtx,
                Policy.ContextWindowTokens);
        }

        return effective;
    }
}
