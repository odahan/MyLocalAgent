namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Identifies a screening failure and carries a safe marked-Base64 fallback.
/// </summary>
public sealed class GuardrailScreeningException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GuardrailScreeningException"/> class.
    /// </summary>
    /// <param name="message">A content-free failure description.</param>
    /// <param name="innerException">The original screening exception.</param>
    /// <param name="fallbackResult">The complete safe fallback result.</param>
    public GuardrailScreeningException(
        string message,
        Exception innerException,
        ContentTrustResult fallbackResult)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(fallbackResult);
        FallbackResult = fallbackResult;
    }

    /// <summary>
    /// Gets the safe marked-Base64 fallback.
    /// </summary>
    public ContentTrustResult FallbackResult { get; }
}
