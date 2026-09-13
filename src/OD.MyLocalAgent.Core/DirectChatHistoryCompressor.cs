using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>Compresses old Direct Chat turns into a cumulative factual memory.</summary>
public interface IDirectChatHistoryCompressor
{
    /// <summary>
    /// Attempts to advance the persisted memory-summary boundary.
    /// </summary>
    /// <returns><see langword="true"/> when a new summary was stored.</returns>
    Task<bool> CompressAsync(
        DirectChatConversation conversation,
        IReadOnlyList<DirectChatMessage> messages,
        CancellationToken cancellationToken);
}

/// <summary>Uses the active language-model provider to create cumulative chat memory.</summary>
public sealed class DirectChatHistoryCompressor(
    IDirectChatStore store,
    ILlmClientFactory llmClientFactory,
    IOptionsMonitor<AIOptions> aiOptions,
    ITokenEstimator tokenEstimator,
    IOptions<DirectChatOptions> directChatOptions) : IDirectChatHistoryCompressor
{
    private readonly IDirectChatStore _store = store;
    private readonly ILlmClientFactory _llmClientFactory = llmClientFactory;
    private readonly IOptionsMonitor<AIOptions> _aiOptions = aiOptions;
    private readonly ITokenEstimator _tokenEstimator = tokenEstimator;
    private readonly DirectChatOptions _options = directChatOptions.Value;

    /// <inheritdoc />
    public async Task<bool> CompressAsync(
        DirectChatConversation conversation,
        IReadOnlyList<DirectChatMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(messages);
        if (!_options.CompressionEnabled)
        {
            return false;
        }

        var afterCurrentSummary = messages
            .Where(message =>
                message.Sequence > (conversation.SummaryThroughSequence ?? 0) &&
                !string.IsNullOrWhiteSpace(message.Content) &&
                message.State is not DirectChatMessageState.Failed)
            .OrderBy(static message => message.Sequence)
            .ToArray();
        var userMessages = afterCurrentSummary
            .Where(static message => message.Role == DirectChatMessageRole.User)
            .ToArray();
        if (userMessages.Length <= _options.MinimumRecentTurns)
        {
            return false;
        }

        var firstRetainedUser = userMessages[^_options.MinimumRecentTurns];
        var eligibleCandidates = afterCurrentSummary
            .Where(message => message.Sequence < firstRetainedUser.Sequence)
            .ToArray();
        if (eligibleCandidates.Length == 0)
        {
            return false;
        }

        var candidates = SelectCompressionPrefix(
            conversation.SummaryText,
            afterCurrentSummary,
            eligibleCandidates);

        var prompt = BuildCompressionPrompt(conversation.SummaryText, candidates);
        var provider = _llmClientFactory.Create();
        var policy = GenerationPolicyResolver.ResolveInternal(
            InternalGenerationPolicies.DirectChatCompression,
            _aiOptions.CurrentValue.GenerationDefaults);
        var client = new GenerationPolicyChatClient(
            provider.ChatClient,
            policy,
            provider.ProviderName,
            "direct-chat-compression",
            reasoningEnabled: false);
        var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { MaxOutputTokens = _options.MaximumSummaryTokens },
                cancellationToken)
            .ConfigureAwait(false);
        var summary = response.Text?.Trim();
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException(
                "The model returned an empty Direct Chat memory summary.");
        }

        await _store.UpdateSummaryAsync(
                conversation.Id,
                summary,
                candidates[^1].Sequence,
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private DirectChatMessage[] SelectCompressionPrefix(
        string? existingSummary,
        IReadOnlyList<DirectChatMessage> allUnsummarized,
        DirectChatMessage[] eligibleCandidates)
    {
        var contextWindow = _aiOptions.CurrentValue.GenerationDefaults.ContextWindowTokens;
        var targetTokens = (int)Math.Floor(
            contextWindow * _options.CompressionTargetRatio);
        var remainingTokens = allUnsummarized.Sum(
            message => _tokenEstimator.EstimateText(message.Content) + 4);
        if (!string.IsNullOrWhiteSpace(existingSummary))
        {
            remainingTokens += _tokenEstimator.EstimateText(existingSummary);
        }

        if (remainingTokens <= targetTokens)
        {
            return eligibleCandidates;
        }

        var count = 0;
        while (count < eligibleCandidates.Length && remainingTokens > targetTokens)
        {
            remainingTokens -=
                _tokenEstimator.EstimateText(eligibleCandidates[count].Content) + 4;
            count++;
        }

        // Never split a user/assistant turn when the assistant reply is eligible.
        while (count < eligibleCandidates.Length &&
               eligibleCandidates[count].Role == DirectChatMessageRole.Assistant)
        {
            count++;
        }

        return eligibleCandidates[..Math.Max(1, count)];
    }

    private static string BuildCompressionPrompt(
        string? existingSummary,
        IReadOnlyList<DirectChatMessage> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "Create an updated, compact memory for a continuing conversation. Return only the memory, without commentary or Markdown fences.");
        builder.AppendLine(
            "Preserve user preferences and constraints, important facts/names/paths/values, decisions, corrections, unresolved tasks, and context needed for future replies.");
        builder.AppendLine(
            "Prefer newer information when it corrects older information. Do not invent facts.");
        if (!string.IsNullOrWhiteSpace(existingSummary))
        {
            builder.AppendLine();
            builder.AppendLine("EXISTING MEMORY:");
            builder.AppendLine(existingSummary.Trim());
        }

        builder.AppendLine();
        builder.AppendLine("NEWLY ARCHIVED MESSAGES:");
        foreach (var message in candidates)
        {
            builder.Append(message.Role == DirectChatMessageRole.User ? "USER: " : "ASSISTANT: ");
            builder.AppendLine(message.Content.Trim());
        }

        return builder.ToString();
    }
}
