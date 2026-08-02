using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes one editable <c>ai.prompt</c> step extracted from a workflow.
/// </summary>
public sealed record WorkflowPromptEntry
{
    /// <summary>
    /// Gets the zero-based workflow position, which is the authoritative edit key.
    /// </summary>
    public required int Position { get; init; }

    /// <summary>
    /// Gets the one-based position shown to the user.
    /// </summary>
    public int DisplayPosition => Position + 1;

    /// <summary>
    /// Gets the explicit step identifier, or <see langword="null"/> when it is implicit.
    /// </summary>
    public string? ExplicitId { get; init; }

    /// <summary>
    /// Gets the effective step identifier.
    /// </summary>
    public required string EffectiveId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the effective identifier is derived from the alias.
    /// </summary>
    public bool IsImplicitId => string.IsNullOrWhiteSpace(ExplicitId);

    /// <summary>
    /// Gets the LF-normalized prompt working text.
    /// </summary>
    public required string PromptText { get; init; }

    /// <summary>
    /// Gets a value indicating whether the accumulated workflow context is sent to the model.
    /// </summary>
    public required bool IncludeContext { get; init; }

    /// <summary>
    /// Gets a value indicating whether the stored prompt contained a carriage return and must
    /// be normalized before it can be saved.
    /// </summary>
    /// <remarks>This counts as an effective modification so Apply stays available.</remarks>
    public bool RequiresLineEndingNormalization { get; init; }

    /// <summary>
    /// Gets a value indicating whether the stored step lacked a usable prompt value.
    /// </summary>
    public bool IsRepairCase { get; init; }

    /// <summary>
    /// Gets the step error policy, for display only.
    /// </summary>
    public WorkflowErrorPolicy? OnError { get; init; }

    /// <summary>
    /// Gets the step retry count, for display only.
    /// </summary>
    public int? RetryCount { get; init; }

    /// <summary>
    /// Gets the step retry backoff in seconds, for display only.
    /// </summary>
    public int? RetryBackoffSeconds { get; init; }

    /// <summary>
    /// Gets the step generation options, for display only.
    /// </summary>
    public GenerationOptions? GenerationOptions { get; init; }

    /// <summary>
    /// Gets the effective identifiers declared before this step.
    /// </summary>
    public IReadOnlyList<string> PriorStepIds { get; init; } = [];
}
