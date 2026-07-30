namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Provides the fixed policies used by deterministic internal language-model operations.
/// </summary>
public static class InternalGenerationPolicies
{
    /// <summary>
    /// Gets the workflow generation, refinement, and repair policy.
    /// </summary>
    public static InternalGenerationPolicy WorkflowAuthoring { get; } =
        new("WorkflowAuthoring", 0, 1);

    /// <summary>
    /// Gets the workflow explanation and explanation-repair policy.
    /// </summary>
    public static InternalGenerationPolicy WorkflowExplanation { get; } =
        new("WorkflowExplanation", 0, 1);

    /// <summary>
    /// Gets the Guardrails screening policy.
    /// </summary>
    public static InternalGenerationPolicy GuardrailScreening { get; } =
        new("GuardrailScreening", 0, 1);

    /// <summary>
    /// Gets the Guardrails reduction policy.
    /// </summary>
    public static InternalGenerationPolicy GuardrailReduction { get; } =
        new("GuardrailReduction", 0, 1);
}
