namespace OD.MyLocalAgent.Core;

/// <summary>
/// Runs the local unattended agent with a per-execution provider and tool whitelist.
/// </summary>
public interface IMyLocalAgentService
{
    /// <summary>
    /// Runs a prompt and returns the final concise report.
    /// </summary>
    /// <param name="prompt">The task prompt.</param>
    /// <param name="enabledTools">The task tool-group whitelist.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The agent result.</returns>
    Task<AgentRunResult> RunPromptAsync(
        string prompt,
        IEnumerable<string> enabledTools,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a prompt and streams response text.
    /// </summary>
    /// <param name="prompt">The task prompt.</param>
    /// <param name="enabledTools">The task tool-group whitelist.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The streamed text chunks.</returns>
    IAsyncEnumerable<string> RunPromptStreamingAsync(
        string prompt,
        IEnumerable<string> enabledTools,
        CancellationToken cancellationToken);
}
