namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Carries a complete-source reduction result for caller-controlled delivery.
/// </summary>
/// <param name="Status">The reduction status.</param>
/// <param name="Summary">The raw generated summary for source-specific canonicalization.</param>
/// <param name="OriginalCharacterCount">The source character count.</param>
/// <param name="SummaryCharacterCount">The delivered summary character count.</param>
/// <param name="SourceTrustScore">The minimum trust score observed across reduction inputs.</param>
/// <param name="SuspiciousIndicators">Validated human-only suspicious spans.</param>
/// <param name="ProviderName">The provider display name.</param>
/// <param name="LatencyMs">The total provider-call latency in milliseconds.</param>
public sealed record ContentSummaryResult(
    ContentSummaryStatus Status,
    string Summary,
    int OriginalCharacterCount,
    int SummaryCharacterCount,
    int? SourceTrustScore,
    IReadOnlyList<string> SuspiciousIndicators,
    string ProviderName,
    long LatencyMs);
