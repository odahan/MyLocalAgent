namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Carries the per-item outcome of compact external-content processing.
/// </summary>
/// <param name="Id">The input opaque correlation identifier.</param>
/// <param name="Summary">The screened factual summary, when deliverable.</param>
/// <param name="SourceTrust">The compact-source trust result.</param>
/// <param name="SummaryTrust">The effective final-summary trust result.</param>
/// <param name="Status">The processing outcome.</param>
/// <param name="RequiresHumanReview">Whether the original source should be reviewed.</param>
/// <param name="LatencyMs">The aggregate provider latency for this item.</param>
public sealed record CompactExternalContentResult(
    string Id,
    string? Summary,
    ContentTrustResult SourceTrust,
    ContentTrustResult? SummaryTrust,
    CompactExternalContentStatus Status,
    bool RequiresHumanReview,
    long LatencyMs);
