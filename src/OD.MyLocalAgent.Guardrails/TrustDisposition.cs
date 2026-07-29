namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Identifies the delivery treatment required by a trust verdict.
/// </summary>
public enum TrustDisposition
{
    /// <summary>
    /// Deliver content inside the mandatory plaintext trust envelope.
    /// </summary>
    PassThrough,

    /// <summary>
    /// Deliver content inside the mandatory Base64 trust envelope.
    /// </summary>
    EncodeBase64,

    /// <summary>
    /// Withhold the external content.
    /// </summary>
    Withhold,
}
