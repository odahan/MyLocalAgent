namespace OD.MyLocalAgent.Ollama;

/// <summary>Describes the outcome of one provider reasoning-effort probe.</summary>
public enum ReasoningEffortValidationStatus
{
    /// <summary>The provider accepted the request syntax.</summary>
    Accepted,

    /// <summary>The provider explicitly rejected the reasoning option.</summary>
    Rejected,

    /// <summary>The provider could not be reached or returned an unrelated failure.</summary>
    Unavailable,
}

/// <summary>Contains the outcome of one provider reasoning-effort probe.</summary>
/// <param name="Status">The validation status.</param>
/// <param name="Message">A concise diagnostic safe for display.</param>
public sealed record ReasoningEffortValidationResult(
    ReasoningEffortValidationStatus Status,
    string Message);

/// <summary>Validates exact reasoning-effort keywords against the active provider and model.</summary>
public interface IReasoningEffortValidator
{
    /// <summary>Validates an exact provider keyword, using a cached result when available.</summary>
    Task<ReasoningEffortValidationResult> ValidateAsync(
        string model,
        string reasoningEffort,
        CancellationToken cancellationToken);
}
