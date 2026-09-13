namespace OD.MyLocalAgent.Ollama;

/// <summary>Identifies one category of application-owned language-model work.</summary>
public enum InternalThinkingCategory
{
    /// <summary>Workflow generation, refinement, and repair.</summary>
    WorkflowAuthoring,
    /// <summary>Workflow explanation and explanation repair.</summary>
    WorkflowExplanation,
    /// <summary>Content screening and reduction.</summary>
    Guardrails,
    /// <summary>Direct Chat history compression.</summary>
    DirectChatCompression,
    /// <summary>Research-candidate translation.</summary>
    ResearchTranslation,
}

/// <summary>Defines how one internal category instructs provider reasoning.</summary>
public enum InternalThinkingMode
{
    /// <summary>Explicitly disable provider reasoning.</summary>
    Disabled,
    /// <summary>Send no reasoning instruction to the provider.</summary>
    ProviderDefault,
    /// <summary>Enable reasoning with one provider-specific level.</summary>
    SpecificLevel,
}

/// <summary>Defines the reasoning preference for one internal operation category.</summary>
public sealed record InternalThinkingSetting
{
    /// <summary>Gets the selected reasoning behavior.</summary>
    public InternalThinkingMode Mode { get; init; } = InternalThinkingMode.Disabled;

    /// <summary>Gets the provider-specific level used with <see cref="InternalThinkingMode.SpecificLevel"/>.</summary>
    public string? Effort { get; init; }
}

/// <summary>Groups independent thinking settings for application-owned operations.</summary>
public sealed record InternalThinkingOptions
{
    /// <summary>Gets the workflow authoring setting.</summary>
    public InternalThinkingSetting WorkflowAuthoring { get; init; } = new();
    /// <summary>Gets the workflow explanation setting.</summary>
    public InternalThinkingSetting WorkflowExplanation { get; init; } = new();
    /// <summary>Gets the Guardrails setting.</summary>
    public InternalThinkingSetting Guardrails { get; init; } = new();
    /// <summary>Gets the Direct Chat compression setting.</summary>
    public InternalThinkingSetting DirectChatCompression { get; init; } = new();
    /// <summary>Gets the research translation setting.</summary>
    public InternalThinkingSetting ResearchTranslation { get; init; } = new();

    /// <summary>Gets the setting associated with a category.</summary>
    public InternalThinkingSetting Get(InternalThinkingCategory category) => category switch
    {
        InternalThinkingCategory.WorkflowAuthoring => WorkflowAuthoring,
        InternalThinkingCategory.WorkflowExplanation => WorkflowExplanation,
        InternalThinkingCategory.Guardrails => Guardrails,
        InternalThinkingCategory.DirectChatCompression => DirectChatCompression,
        InternalThinkingCategory.ResearchTranslation => ResearchTranslation,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };
}
