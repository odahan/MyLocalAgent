using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>Estimates provider-independent token usage for Direct Chat context management.</summary>
public interface ITokenEstimator
{
    /// <summary>Estimates tokens occupied by arbitrary text.</summary>
    int EstimateText(string text);

    /// <summary>Estimates tokens occupied by a role-tagged message collection.</summary>
    int EstimateMessages(IEnumerable<ChatMessage> messages);
}

/// <summary>Uses a conservative character-ratio token estimate.</summary>
public sealed class ConservativeTokenEstimator(IOptions<DirectChatOptions> options)
    : ITokenEstimator
{
    private readonly DirectChatOptions _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public int EstimateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Math.Max(
            1,
            (int)Math.Ceiling(
                text.Length / (double)_options.EstimatedCharactersPerToken));
    }

    /// <inheritdoc />
    public int EstimateMessages(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return messages.Sum(message => EstimateText(message.Text ?? string.Empty) + 4) + 2;
    }
}

/// <summary>Contains the provider messages and capacity assessment for one request.</summary>
public sealed record DirectChatContextPlan(
    IReadOnlyList<ChatMessage> Messages,
    int EstimatedInputTokens,
    int CompressionTriggerTokens,
    int MaximumInputTokens)
{
    /// <summary>Gets a value indicating whether history compression should be attempted.</summary>
    public bool RequiresCompression => EstimatedInputTokens >= CompressionTriggerTokens;

    /// <summary>Gets a value indicating whether the input leaves the configured response reserve.</summary>
    public bool FitsWithResponseReserve => EstimatedInputTokens <= MaximumInputTokens;
}

/// <summary>Builds provider-ready chat context from persisted conversation memory.</summary>
public interface IDirectChatContextBuilder
{
    /// <summary>Builds a context plan for the supplied conversation state.</summary>
    DirectChatContextPlan Build(
        DirectChatConversation conversation,
        IReadOnlyList<DirectChatMessage> messages);
}

/// <summary>Builds Direct Chat context from system instructions, memory, and recent turns.</summary>
public sealed class DirectChatContextBuilder(
    ITokenEstimator tokenEstimator,
    IOptionsMonitor<AIOptions> aiOptions,
    IOptions<DirectChatOptions> directChatOptions) : IDirectChatContextBuilder
{
    private const string DirectChatSystemPrompt =
        "You are a helpful assistant in a private direct conversation. Use the supplied conversation memory and recent messages consistently. If memory conflicts with a newer message, follow the newer message.";
    private readonly ITokenEstimator _tokenEstimator = tokenEstimator;
    private readonly IOptionsMonitor<AIOptions> _aiOptions = aiOptions;
    private readonly DirectChatOptions _directChatOptions = directChatOptions.Value;

    /// <inheritdoc />
    public DirectChatContextPlan Build(
        DirectChatConversation conversation,
        IReadOnlyList<DirectChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(messages);
        var systemInstructions = new List<string>
        {
            DirectChatSystemPrompt,
        };
        var options = _aiOptions.CurrentValue;
        if (!string.IsNullOrWhiteSpace(options.EffectiveDirectChatSystemPrompt))
        {
            systemInstructions.Add(options.EffectiveDirectChatSystemPrompt.Trim());
        }

        if (!string.IsNullOrWhiteSpace(conversation.SummaryText))
        {
            systemInstructions.Add(
                "Conversation memory summarizing older messages:\n" +
                conversation.SummaryText.Trim());
        }

        // Some Ollama chat templates accept exactly one system message, and require it
        // to be the first message. Consolidate all Direct Chat system context here.
        var providerMessages = new List<ChatMessage>
        {
            new(ChatRole.System, string.Join("\n\n", systemInstructions)),
        };

        var summarizedThrough = conversation.SummaryThroughSequence ?? 0;
        foreach (var message in messages.Where(message =>
                     message.Sequence > summarizedThrough &&
                     !string.IsNullOrWhiteSpace(message.Content) &&
                     message.State is not DirectChatMessageState.Failed))
        {
            var contents = new List<AIContent> { new TextContent(message.Content) };
            foreach (var attachment in message.Attachments)
            {
                if (attachment.Kind == DirectChatAttachmentKind.Text &&
                    !string.IsNullOrWhiteSpace(attachment.Text))
                {
                    contents.Add(new TextContent($"\nAttached file ({attachment.FileName}):\n{attachment.Text}"));
                }
                else if (attachment.Kind == DirectChatAttachmentKind.Image &&
                         attachment.Data is { Length: > 0 })
                {
                    contents.Add(new DataContent(attachment.Data, attachment.MediaType));
                }
            }

            providerMessages.Add(new ChatMessage(
                message.Role == DirectChatMessageRole.User ? ChatRole.User : ChatRole.Assistant,
                contents));
        }

        var contextWindow = options.GenerationDefaults.ContextWindowTokens;
        return new DirectChatContextPlan(
            providerMessages,
            _tokenEstimator.EstimateMessages(providerMessages),
            Math.Max(
                1,
                (int)Math.Floor(
                    contextWindow * _directChatOptions.CompressionTriggerRatio)),
            Math.Max(
                1,
                (int)Math.Floor(
                    contextWindow * (1 - _directChatOptions.ResponseReserveRatio))));
    }
}
