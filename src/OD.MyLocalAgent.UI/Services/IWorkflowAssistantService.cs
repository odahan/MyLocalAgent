namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Streams tool-less workflow generation and explanation responses.
/// </summary>
public interface IWorkflowAssistantService
{
    /// <summary>
    /// Streams one generation, refinement, or deterministic-repair attempt.
    /// </summary>
    /// <param name="userMessage">The complete operation-specific user message.</param>
    /// <param name="cancellationToken">A token that cancels streaming.</param>
    /// <returns>The exact response text chunks.</returns>
    IAsyncEnumerable<string> StreamGenerationAsync(
        string userMessage,
        CancellationToken cancellationToken);

    /// <summary>
    /// Streams one explanation or retry attempt.
    /// </summary>
    /// <param name="userMessage">The canonical workflow and deterministic analysis.</param>
    /// <param name="cancellationToken">A token that cancels streaming.</param>
    /// <returns>The exact response text chunks.</returns>
    IAsyncEnumerable<string> StreamExplanationAsync(
        string userMessage,
        CancellationToken cancellationToken);
}
