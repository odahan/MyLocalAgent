using Microsoft.Extensions.AI;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Provides provider-agnostic language-model generation operations.
/// </summary>
public interface ILlmClientService
{
    /// <summary>
    /// Gets the provider chat client used by the agent layer.
    /// </summary>
    IChatClient ChatClient { get; }

    /// <summary>
    /// Gets the provider display name.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Generates a complete response for a prompt.
    /// </summary>
    /// <param name="prompt">The user prompt.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The generated response text.</returns>
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken);

    /// <summary>
    /// Streams response text for a prompt.
    /// </summary>
    /// <param name="prompt">The user prompt.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>An asynchronous sequence of text chunks.</returns>
    IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks endpoint and configured-model health.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The provider health result.</returns>
    Task<LlmHealthResult> CheckHealthAsync(CancellationToken cancellationToken);
}
