namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Represents missing or invalid connector credentials.
/// </summary>
public sealed class ConnectorConfigurationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectorConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The actionable configuration error.</param>
    public ConnectorConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectorConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The actionable configuration error.</param>
    /// <param name="innerException">The underlying options error.</param>
    public ConnectorConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
