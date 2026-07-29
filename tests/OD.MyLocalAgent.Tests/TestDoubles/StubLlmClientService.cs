using Microsoft.Extensions.AI;
using OD.MyLocalAgent.Ollama;
using System.Runtime.CompilerServices;

namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class StubLlmClientService : ILlmClientService
{
    private readonly string _providerName;

    public StubLlmClientService(string response)
        : this(new StubChatClient(response), "Stub")
    {
    }

    public StubLlmClientService(StubChatClient client, string providerName)
    {
        Client = client;
        _providerName = providerName;
    }

    public StubChatClient Client { get; }

    public IChatClient ChatClient => Client;

    public string ProviderName => _providerName;

    public async Task<string> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        var response = await Client.GetResponseAsync(
            prompt,
            cancellationToken: cancellationToken);
        return response.Text;
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        var response = await Client.GetResponseAsync(
            prompt,
            cancellationToken: cancellationToken);
        yield return response.Text;
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
