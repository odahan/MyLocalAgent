namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Carries a trust verdict and its conservatively wrapped delivery content.
/// </summary>
/// <param name="TrustScore">The clamped trust score for screened content.</param>
/// <param name="Content">The wrapped or withheld content.</param>
/// <param name="ContentEncoding">The encoding of the payload inside the wrapper.</param>
/// <param name="Status">The screening status.</param>
/// <param name="SuspiciousIndicators">Validated human-only suspicious spans.</param>
/// <param name="ProviderName">The provider display name.</param>
/// <param name="LatencyMs">The provider-call latency in milliseconds.</param>
public sealed record ContentTrustResult(
    int? TrustScore,
    string Content,
    string ContentEncoding,
    ContentTrustStatus Status,
    IReadOnlyList<string> SuspiciousIndicators,
    string ProviderName,
    long LatencyMs)
{
    /// <summary>
    /// Gets whether a valid LLM verdict was returned.
    /// </summary>
    public bool Screened => Status == ContentTrustStatus.Screened;
}
