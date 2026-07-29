namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Identifies whether content received an LLM trust verdict.
/// </summary>
public enum ContentTrustStatus
{
    /// <summary>
    /// The active provider returned a valid trust verdict.
    /// </summary>
    Screened,

    /// <summary>
    /// LLM processing is disabled and conservative delivery was applied.
    /// </summary>
    ScreeningDisabled,

    /// <summary>
    /// Screening failed and conservative delivery was applied.
    /// </summary>
    Failed,
}
