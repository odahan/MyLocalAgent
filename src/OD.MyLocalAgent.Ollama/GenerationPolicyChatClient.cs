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

    /// <summary>
    /// Initializes a new policy-bound chat client.
    /// </summary>
    /// <param name="inner">The shared provider chat client.</param>
    /// <param name="policy">The immutable effective policy.</param>
    /// <param name="providerName">The selected provider name.</param>
    public GenerationPolicyChatClient(
        IChatClient inner,
        EffectiveGenerationPolicy policy,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        _inner = inner;
        Policy = policy;
        _mapsOllamaContext = string.Equals(
            providerName,
            AIOptions.OllamaProvider,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the immutable policy applied by this wrapper.
    /// </summary>
    public EffectiveGenerationPolicy Policy { get; }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _inner.GetResponseAsync(
            messages,
            CreateOptions(options),
            cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in _inner
            .GetStreamingResponseAsync(
                messages,
                CreateOptions(options),
                cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
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
