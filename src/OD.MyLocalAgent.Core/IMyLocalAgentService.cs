namespace OD.MyLocalAgent.Core;

using OD.MyLocalAgent.Ollama;

/// <summary>
/// Runs the local unattended agent with a per-execution provider and tool whitelist.
/// </summary>
public interface IMyLocalAgentService
{
    /// <summary>
    /// Generates one model response without exposing any tool or entering a tool-call loop.
    /// </summary>
    /// <param name="prompt">The complete prompt.</param>
    /// <param name="generationPolicy">The immutable effective generation policy.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The model result.</returns>
    Task<AgentRunResult> RunPromptWithoutToolsAsync(
        string prompt,
        EffectiveGenerationPolicy generationPolicy,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generates one model response without tools and assigns its usage to a logical operation.
    /// </summary>
    /// <param name="prompt">The complete prompt.</param>
    /// <param name="generationPolicy">The immutable effective generation policy.</param>
    /// <param name="logicalOperation">The logical operation or workflow step identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The model result.</returns>
    Task<AgentRunResult> RunPromptWithoutToolsAsync(
        string prompt,
        EffectiveGenerationPolicy generationPolicy,
        string logicalOperation,
        CancellationToken cancellationToken) =>
        RunPromptWithoutToolsAsync(prompt, generationPolicy, cancellationToken);

    /// <summary>
    /// Generates one model response without tools with an explicit project-instructions preference.
    /// </summary>
    Task<AgentRunResult> RunPromptWithoutToolsAsync(
        string prompt,
        EffectiveGenerationPolicy generationPolicy,
        string logicalOperation,
        bool useGlobalSystemPrompt,
        CancellationToken cancellationToken) =>
        RunPromptWithoutToolsAsync(prompt, generationPolicy, logicalOperation, cancellationToken);

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
    /// Runs a task prompt with an immutable tool execution context.
    /// </summary>
    /// <param name="prompt">The task prompt.</param>
    /// <param name="enabledTools">The task tool-group whitelist.</param>
    /// <param name="executionContext">The immutable tool execution context.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The agent result.</returns>
    Task<AgentRunResult> RunPromptAsync(
        string prompt,
        IEnumerable<string> enabledTools,
        ToolExecutionContext executionContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a task prompt with immutable tool and generation contexts.
    /// </summary>
    /// <param name="prompt">The task prompt.</param>
    /// <param name="enabledTools">The task tool-group whitelist.</param>
    /// <param name="executionContext">The immutable tool execution context.</param>
    /// <param name="generationPolicy">The immutable effective generation policy.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The agent result.</returns>
    Task<AgentRunResult> RunPromptAsync(
        string prompt,
        IEnumerable<string> enabledTools,
        ToolExecutionContext executionContext,
        EffectiveGenerationPolicy generationPolicy,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a prompt with an optional interactive skill catalog and returns the final report.
    /// </summary>
    /// <param name="prompt">The task prompt.</param>
    /// <param name="enabledTools">The tool-group whitelist.</param>
    /// <param name="allowSkills">
    /// Whether the agent may invoke the built-in reusable skills.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The agent result.</returns>
    Task<AgentRunResult> RunPromptAsync(
        string prompt,
        IEnumerable<string> enabledTools,
        bool allowSkills,
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

    /// <summary>
    /// Runs a task prompt with contextual tools and streams response text.
    /// </summary>
    /// <param name="prompt">The task prompt.</param>
    /// <param name="enabledTools">The task tool-group whitelist.</param>
    /// <param name="executionContext">The immutable tool execution context.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The streamed text chunks.</returns>
    IAsyncEnumerable<string> RunPromptStreamingAsync(
        string prompt,
        IEnumerable<string> enabledTools,
        ToolExecutionContext executionContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a task prompt with immutable tool and generation contexts and streams text.
    /// </summary>
    /// <param name="prompt">The task prompt.</param>
    /// <param name="enabledTools">The task tool-group whitelist.</param>
    /// <param name="executionContext">The immutable tool execution context.</param>
    /// <param name="generationPolicy">The immutable effective generation policy.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The streamed text chunks.</returns>
    IAsyncEnumerable<string> RunPromptStreamingAsync(
        string prompt,
        IEnumerable<string> enabledTools,
        ToolExecutionContext executionContext,
        EffectiveGenerationPolicy generationPolicy,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a prompt and streams text with an optional interactive skill catalog.
    /// </summary>
    /// <param name="prompt">The task prompt.</param>
    /// <param name="enabledTools">The tool-group whitelist.</param>
    /// <param name="allowSkills">
    /// Whether the agent may invoke the built-in reusable skills.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The streamed text chunks.</returns>
    IAsyncEnumerable<string> RunPromptStreamingAsync(
        string prompt,
        IEnumerable<string> enabledTools,
        bool allowSkills,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs an interactive prompt with optional skills and an explicit global policy.
    /// </summary>
    /// <param name="prompt">The interactive prompt.</param>
    /// <param name="enabledTools">The tool-group whitelist.</param>
    /// <param name="allowSkills">Whether built-in skills may be invoked.</param>
    /// <param name="generationPolicy">The immutable effective generation policy.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The streamed text chunks.</returns>
    IAsyncEnumerable<string> RunPromptStreamingAsync(
        string prompt,
        IEnumerable<string> enabledTools,
        bool allowSkills,
        EffectiveGenerationPolicy generationPolicy,
        CancellationToken cancellationToken);
}
