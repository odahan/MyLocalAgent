namespace OD.MyLocalAgent.Core;

using OD.MyLocalAgent.Ollama;

/// <summary>
/// Represents a named reusable plain-completion prompt template.
/// </summary>
public interface ISkill
{
    /// <summary>
    /// Gets the workflow skill name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the function name used when the skill is enabled in an interactive agent.
    /// </summary>
    string AgentFunctionName { get; }

    /// <summary>
    /// Gets the agent-facing description of the skill.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Renders and runs the skill without tools.
    /// </summary>
    /// <param name="text">The untrusted input text.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The generated text.</returns>
    Task<string> ExecuteAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Renders and runs the skill with an explicit immutable generation policy.
    /// </summary>
    /// <param name="text">The untrusted input text.</param>
    /// <param name="generationPolicy">The effective generation policy.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The generated text.</returns>
    Task<string> ExecuteAsync(
        string text,
        EffectiveGenerationPolicy generationPolicy,
        CancellationToken cancellationToken) =>
        ExecuteAsync(text, cancellationToken);
}
