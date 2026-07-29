namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Identifies a complete-source reduction failure without carrying external content.
/// </summary>
public sealed class GuardrailSummarizationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GuardrailSummarizationException"/> class.
    /// </summary>
    /// <param name="message">A content-free failure description.</param>
    /// <param name="innerException">The original reduction exception.</param>
    public GuardrailSummarizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
