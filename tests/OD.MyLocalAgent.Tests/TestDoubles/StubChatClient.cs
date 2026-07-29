using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class StubChatClient : IChatClient
{
    private readonly Queue<string> _responses;

    public StubChatClient(string response)
    {
        _responses = new Queue<string>([response]);
    }

    public StubChatClient(IEnumerable<string> responses)
    {
        _responses = new Queue<string>(responses);
        if (_responses.Count == 0)
        {
            throw new ArgumentException("At least one response is required.", nameof(responses));
        }
    }

    public ChatOptions? LastOptions { get; private set; }

    public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

    public List<IReadOnlyList<ChatMessage>> MessageCalls { get; } = [];

    public List<ChatOptions?> OptionCalls { get; } = [];

    public int CallCount { get; private set; }

    public Exception? ExceptionToThrow { get; set; }

    public bool WaitForCancellation { get; set; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastMessages = messages.ToArray();
        LastOptions = options;
        MessageCalls.Add(LastMessages);
        OptionCalls.Add(options);
        CallCount++;
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        if (WaitForCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        var response = NextResponse();
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, response));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastMessages = messages.ToArray();
        LastOptions = options;
        MessageCalls.Add(LastMessages);
        OptionCalls.Add(options);
        CallCount++;
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, NextResponse());
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private string NextResponse()
    {
        if (_responses.Count > 1)
        {
            return _responses.Dequeue();
        }

        return _responses.Peek();
    }
}
