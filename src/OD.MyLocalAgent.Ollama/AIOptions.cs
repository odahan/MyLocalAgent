namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Defines the active language-model provider.
/// </summary>
public sealed record AIOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "AI";

    /// <summary>
    /// Gets the Ollama provider name.
    /// </summary>
    public const string OllamaProvider = "Ollama";

    /// <summary>
    /// Gets the OpenAI provider name.
    /// </summary>
    public const string OpenAIProvider = "OpenAI";

    /// <summary>
    /// Gets the configured provider.
    /// </summary>
    public string Provider { get; init; } = OllamaProvider;

    /// <summary>
    /// Gets the reloadable generation defaults shared by the active provider and model.
    /// </summary>
    public AIGenerationDefaults GenerationDefaults { get; init; } = new();

    /// <summary>
    /// Gets the optional instructions included in agentic task calls.
    /// </summary>
    public string AgentSystemPrompt { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional instructions included in Direct Chat calls.
    /// </summary>
    public string DirectChatSystemPrompt { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional instructions included in workflow <c>ai.prompt</c> calls.
    /// </summary>
    public string PromptStepSystemPrompt { get; init; } = string.Empty;

    /// <summary>
    /// Gets the legacy project-wide instructions used as a migration fallback.
    /// </summary>
    public string GlobalSystemPrompt { get; init; } = string.Empty;

    /// <summary>Gets the effective agent instructions, including legacy fallback.</summary>
    public string EffectiveAgentSystemPrompt =>
        ResolveSpecializedPrompt(AgentSystemPrompt);

    /// <summary>Gets the effective Direct Chat instructions, including legacy fallback.</summary>
    public string EffectiveDirectChatSystemPrompt =>
        ResolveSpecializedPrompt(DirectChatSystemPrompt);

    /// <summary>Gets the effective prompt-step instructions, including legacy fallback.</summary>
    public string EffectivePromptStepSystemPrompt =>
        ResolveSpecializedPrompt(PromptStepSystemPrompt);

    private string ResolveSpecializedPrompt(string specializedPrompt) =>
        string.IsNullOrWhiteSpace(specializedPrompt)
            ? GlobalSystemPrompt
            : specializedPrompt;
}
