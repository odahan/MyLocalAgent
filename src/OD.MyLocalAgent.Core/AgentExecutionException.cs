namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents an agent startup failure that has already been reported as an incident.
/// </summary>
public sealed class AgentExecutionException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AgentExecutionException"/> class.
    /// </summary>
    /// <param name="correlationId">The reported incident correlation identifier.</param>
    /// <param name="innerException">The underlying configuration failure.</param>
    public AgentExecutionException(string correlationId, Exception innerException)
        : base(
            $"The AI provider could not be started. Review Settings and try again. Incident: {correlationId}.",
            innerException)
    {
        CorrelationId = correlationId;
    }

    /// <summary>
    /// Gets the incident correlation identifier.
    /// </summary>
    public string CorrelationId { get; }
}
