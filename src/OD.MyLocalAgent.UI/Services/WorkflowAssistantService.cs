using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Calls the current provider directly with two messages, streaming, and no tools.
/// </summary>
public sealed class WorkflowAssistantService : IWorkflowAssistantService
{
    private readonly ILlmClientFactory _clientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowAssistantService"/> class.
    /// </summary>
    /// <param name="clientFactory">The hot-switching provider factory.</param>
    public WorkflowAssistantService(ILlmClientFactory clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<string> StreamGenerationAsync(
        string userMessage,
        CancellationToken cancellationToken) =>
        StreamAsync(
            WorkflowAssistantPromptBuilder.BuildGenerationSystemPrompt(),
            userMessage,
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<string> StreamExplanationAsync(
        string userMessage,
        CancellationToken cancellationToken) =>
        StreamAsync(
            WorkflowAssistantPromptBuilder.BuildExplanationSystemPrompt(),
            userMessage,
            cancellationToken);

    private async IAsyncEnumerable<string> StreamAsync(
        string systemMessage,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        var provider = _clientFactory.Create();
        await foreach (var update in provider.ChatClient
            .GetStreamingResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemMessage),
                    new ChatMessage(ChatRole.User, userMessage),
                ],
                new ChatOptions
                {
                    Temperature = 0,
                    Tools = null,
                },
                cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }
}
