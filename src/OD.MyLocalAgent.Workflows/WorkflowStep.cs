using System.Text.Json;
using OD.MyLocalAgent.Core;

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
}
