using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Calls the current provider directly with two messages, streaming, and no tools.
/// </summary>
public sealed class WorkflowAssistantService : IWorkflowAssistantService
{
    private readonly ILlmClientFactory _clientFactory;
    private readonly IOptionsMonitor<AIOptions>? _aiOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowAssistantService"/> class.
    /// </summary>
    /// <param name="clientFactory">The hot-switching provider factory.</param>
    /// <param name="aiOptions">The optional reloadable global generation defaults.</param>
    public WorkflowAssistantService(
        ILlmClientFactory clientFactory,
        IOptionsMonitor<AIOptions>? aiOptions = null)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
        _aiOptions = aiOptions;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<string> StreamGenerationAsync(
        string userMessage,
        CancellationToken cancellationToken) =>
        StreamAsync(
            WorkflowAssistantPromptBuilder.BuildGenerationSystemPrompt(),
            userMessage,
            InternalGenerationPolicies.WorkflowAuthoring,
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<string> StreamExplanationAsync(
        string userMessage,
        CancellationToken cancellationToken) =>
        StreamAsync(
            WorkflowAssistantPromptBuilder.BuildExplanationSystemPrompt(),
            userMessage,
            InternalGenerationPolicies.WorkflowExplanation,
            cancellationToken);

    private async IAsyncEnumerable<string> StreamAsync(
        string systemMessage,
        string userMessage,
        InternalGenerationPolicy internalPolicy,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        var provider = _clientFactory.Create();
        var generationPolicy = GenerationPolicyResolver.ResolveInternal(
            internalPolicy,
            _aiOptions?.CurrentValue.GenerationDefaults ??
            new AIGenerationDefaults());
        var policyClient = new GenerationPolicyChatClient(
            provider.ChatClient,
            generationPolicy,
            provider.ProviderName,
            "workflow-assistant",
            reasoningEnabled: false);
        await foreach (var update in policyClient
            .GetStreamingResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemMessage),
                    new ChatMessage(ChatRole.User, userMessage),
                ],
                new ChatOptions
                {
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
