using Microsoft.Extensions.AI;
using OD.MyLocalAgent.Ollama;
using System.Runtime.CompilerServices;

namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class StubLlmClientService : ILlmClientService
{
    public StubLlmClientService(string response)
    {
        ChatClient = new StubChatClient(response);
        Response = response;
    }

    public IChatClient ChatClient { get; }

    public string ProviderName => "Stub";

    private string Response { get; }

    public Task<string> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken) =>
        Task.FromResult(Response);

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return Response;
    }

    public Task<LlmHealthResult> CheckHealthAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(
            new LlmHealthResult(
                true,
                true,
                0,
                null));
}
