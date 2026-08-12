using Microsoft.Extensions.AI;

namespace OD.MyLocalAgent.Ollama;

/// <summary>Validates completion metadata returned by OpenAI-compatible streaming endpoints.</summary>
internal static class OpenAIStreamingCompletionValidator
{
    /// <summary>Rejects streams which did not end with a provider-confirmed successful completion.</summary>
    public static void EnsureCompleted(
        ChatFinishReason? finishReason,
        int updateCount,
        int textCharacterCount)
    {
        if (finishReason is { } reason &&
            (reason == ChatFinishReason.Stop || reason == ChatFinishReason.ToolCalls))
        {
            return;
        }

        var reasonText = finishReason?.Value ?? "missing";
        throw new OpenAIStreamingCompletionException(
            $"The OpenAI-compatible provider ended the streaming response without a successful completion reason (reason: {reasonText}; updates: {updateCount}; text characters: {textCharacterCount}).");
    }
}

/// <summary>Represents an incomplete or otherwise unsuccessful streaming provider response.</summary>
public sealed class OpenAIStreamingCompletionException : InvalidOperationException
{
    /// <summary>Initializes an exception with safe provider completion diagnostics.</summary>
    public OpenAIStreamingCompletionException(string message)
        : base(message)
    {
    }
}
