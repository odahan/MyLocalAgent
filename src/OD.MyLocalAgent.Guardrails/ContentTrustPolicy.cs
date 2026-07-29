namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Maps trust verdicts to mandatory external-content delivery treatments.
/// </summary>
public static class ContentTrustPolicy
{
    /// <summary>
    /// Chooses the required delivery treatment.
    /// </summary>
    /// <param name="result">The trust result.</param>
    /// <param name="options">The threshold snapshot.</param>
    /// <returns>The required treatment.</returns>
    public static TrustDisposition Decide(
        ContentTrustResult result,
        GuardrailsOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);

        if (result.Status is ContentTrustStatus.ScreeningDisabled or ContentTrustStatus.Failed)
        {
            return TrustDisposition.EncodeBase64;
        }

        var score = result.TrustScore
            ?? throw new InvalidOperationException(
                "A screened trust result must contain a score.");
        if (score >= options.CleanThreshold)
        {
            return TrustDisposition.PassThrough;
        }

        return score >= options.CautionThreshold
            ? TrustDisposition.EncodeBase64
            : TrustDisposition.Withhold;
    }
}
