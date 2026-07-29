using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class StubChatClient : IChatClient
{
    private readonly string _response;

    public StubChatClient(string response)
    {
        _response = response;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = messages.ToArray();
        return Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = messages.ToArray();
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, _response);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
