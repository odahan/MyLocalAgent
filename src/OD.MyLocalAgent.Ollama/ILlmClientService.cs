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
    /// Generates a complete response with an explicit immutable policy.
    /// </summary>
    /// <param name="prompt">The user prompt.</param>
    /// <param name="policy">The effective generation policy.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The generated response text.</returns>
    async Task<string> GenerateAsync(
        string prompt,
        EffectiveGenerationPolicy policy,
        CancellationToken cancellationToken)
    {
        var policyClient = new GenerationPolicyChatClient(
            ChatClient,
            policy,
            ProviderName);
        var response = await policyClient
            .GetResponseAsync(
                prompt,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.Text;
    }

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
    /// Streams response text with an explicit immutable policy.
    /// </summary>
    /// <param name="prompt">The user prompt.</param>
    /// <param name="policy">The effective generation policy.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>An asynchronous sequence of text chunks.</returns>
    async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        EffectiveGenerationPolicy policy,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        var policyClient = new GenerationPolicyChatClient(
            ChatClient,
            policy,
            ProviderName);
        await foreach (var update in policyClient
                           .GetStreamingResponseAsync(
                               prompt,
                               cancellationToken: cancellationToken)
                           .ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    /// <summary>
    /// Checks endpoint and configured-model health.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The provider health result.</returns>
    Task<LlmHealthResult> CheckHealthAsync(CancellationToken cancellationToken);
}
