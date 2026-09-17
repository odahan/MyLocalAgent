using System.Text.Json;
using System.Text.Json.Serialization;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents the normalized internal form of a workflow step.
/// </summary>
[JsonConverter(typeof(WorkflowStepDefinitionJsonConverter))]
public sealed record WorkflowStepDefinition
{
    /// <summary>
    /// Gets the Tool Map alias.
    /// </summary>
    public required string Step { get; init; }

    /// <summary>
    /// Gets the output identifier, or the alias when no explicit identifier was supplied.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Gets the step arguments.
    /// </summary>
    public JsonElement Args { get; init; }

    /// <summary>
    /// Gets the optional step-specific error policy.
    /// </summary>
    public WorkflowErrorPolicy? OnError { get; init; }

    /// <summary>
    /// Gets the optional retry count.
    /// </summary>
    public int? RetryCount { get; init; }

    /// <summary>
    /// Gets the optional initial retry backoff in seconds.
    /// </summary>
    public int? RetryBackoffSeconds { get; init; }

    /// <summary>
    /// Gets the optional schema-valid JSON output to use after all attempts fail.
    /// This permits a workflow to complete safely when an AI model cannot produce
    /// a valid structured response.
    /// </summary>
    public JsonElement? FallbackOutput { get; init; }

    /// <summary>
    /// Gets the optional AI-step Temperature and Top P overrides.
    /// </summary>
    public GenerationOptions? GenerationOptions { get; init; }

    /// <summary>Gets the optional model used only while this AI step executes.</summary>
    public string? Model { get; init; }

    /// <summary>Gets exact declarative Tool Map aliases available to an <c>ai.prompt</c> step.</summary>
    public IReadOnlyList<string> ToolAliases { get; init; } = [];

    /// <summary>Gets an optional Knowledge scope that narrows the task scope.</summary>
    public KnowledgeAccessDefinition? KnowledgeAccess { get; init; }

    /// <summary>Gets the optional successor step id, or <c>@end</c>.</summary>
    public string? Next { get; init; }

    /// <summary>Gets the optional JSON response schema for an <c>ai.prompt</c> step.</summary>
    public JsonElement? ResponseSchema { get; init; }

    /// <summary>
    /// Gets the optional prior-output selector whose JSON array items execute this step.
    /// </summary>
    public string? ForEach { get; init; }

    /// <summary>
    /// Gets a value indicating whether the step was read from string form.
    /// </summary>
    [JsonIgnore]
    public bool IsStringForm { get; init; }

    /// <summary>
    /// Gets the effective output identifier.
    /// </summary>
    [JsonIgnore]
    public string EffectiveId => string.IsNullOrWhiteSpace(Id) ? Step : Id;
}
