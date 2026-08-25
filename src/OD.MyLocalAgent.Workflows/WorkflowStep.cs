using System.Text.Json;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Represents a normalized executable workflow step.
/// </summary>
public abstract class WorkflowStep
{
    /// <summary>
    /// Gets the default number of retries after the initial attempt.
    /// </summary>
    public const int DefaultRetryCount = 3;

    /// <summary>
    /// Gets the default initial retry backoff in seconds.
    /// </summary>
    public const int DefaultRetryBackoffSeconds = 1;

    /// <summary>
    /// Initializes a normalized workflow step.
    /// </summary>
    /// <param name="definition">The persisted step definition.</param>
    protected WorkflowStep(WorkflowStepDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Alias = definition.Step;
        Id = definition.EffectiveId;
        Args = definition.Args.ValueKind == JsonValueKind.Undefined
            ? default
            : definition.Args.Clone();
        OnError = definition.OnError;
        RetryCount = definition.RetryCount ?? DefaultRetryCount;
        RetryBackoffSeconds =
            definition.RetryBackoffSeconds ?? DefaultRetryBackoffSeconds;
        IsStringForm = definition.IsStringForm;
        GenerationOptions = definition.GenerationOptions;
        Model = definition.Model;
        ToolAliases = definition.ToolAliases.ToArray();
        KnowledgeAccess = definition.KnowledgeAccess;
        Next = definition.Next;
        ResponseSchema = definition.ResponseSchema?.Clone();
        ForEach = definition.ForEach;
    }

    /// <summary>
    /// Gets the Tool Map alias.
    /// </summary>
    public string Alias { get; }

    /// <summary>
    /// Gets the output identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the JSON arguments supplied to the step.
    /// </summary>
    public JsonElement Args { get; }

    /// <summary>
    /// Gets the optional step-specific error policy.
    /// </summary>
    public WorkflowErrorPolicy? OnError { get; }

    /// <summary>
    /// Gets the number of retries after the initial attempt.
    /// </summary>
    public int RetryCount { get; }

    /// <summary>
    /// Gets the initial retry backoff in seconds.
    /// </summary>
    public int RetryBackoffSeconds { get; }

    /// <summary>
    /// Gets a value indicating whether the persisted definition used string form.
    /// </summary>
    public bool IsStringForm { get; }

    /// <summary>
    /// Gets the optional AI-step generation overrides.
    /// </summary>
    public GenerationOptions? GenerationOptions { get; }

    /// <summary>Gets the optional model selected for this AI step.</summary>
    public string? Model { get; }

    /// <summary>Gets exact tool aliases available inside this prompt step.</summary>
    public IReadOnlyList<string> ToolAliases { get; }

    /// <summary>Gets the optional prompt-step Knowledge narrowing.</summary>
    public KnowledgeAccessDefinition? KnowledgeAccess { get; }

    /// <summary>Gets the optional successor step id, or <c>@end</c>.</summary>
    public string? Next { get; }

    /// <summary>Gets the optional structured response schema.</summary>
    public JsonElement? ResponseSchema { get; }

    /// <summary>Gets the optional JSON-array selector used to repeat this step.</summary>
    public string? ForEach { get; }
}
