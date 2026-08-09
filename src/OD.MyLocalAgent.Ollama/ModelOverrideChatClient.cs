using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace OD.MyLocalAgent.Ollama;

/// <summary>Forces a request scope to use a selected model without changing provider settings.</summary>
public sealed class ModelOverrideChatClient(IChatClient inner, string modelName) : IChatClient
{
    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => await inner.GetResponseAsync(messages, WithModel(options), cancellationToken).ConfigureAwait(false);
    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    { await foreach (var update in inner.GetStreamingResponseAsync(messages, WithModel(options), cancellationToken).ConfigureAwait(false)) yield return update; }
    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => inner.GetService(serviceType, serviceKey);
    /// <inheritdoc />
    public void Dispose() { }
    private ChatOptions WithModel(ChatOptions? options) { var effective = options?.Clone() ?? new ChatOptions(); effective.ModelId = modelName; return effective; }
}
