namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Identifies the outcome of compact external-content processing.
/// </summary>
public enum CompactExternalContentStatus
{
    /// <summary>The source and generated summary passed screening.</summary>
    Summarized,

    /// <summary>The compact source was below the delivery threshold.</summary>
    SourceWithheld,

    /// <summary>Source screening was disabled or unavailable.</summary>
    SourceScreeningUnavailable,

    /// <summary>The eligible compact source could not be summarized.</summary>
    SummarizationUnavailable,

    /// <summary>The generated summary could not be screened safely.</summary>
    FinalScreeningUnavailable,

    /// <summary>The source passed screen-only classification without summarization.</summary>
    Screened,
}
