namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Represents an unavailable or timed-out Ollama endpoint.
/// </summary>
public sealed class OllamaUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OllamaUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The provider error message.</param>
    public OllamaUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OllamaUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The provider error message.</param>
    /// <param name="innerException">The underlying transport error.</param>
    public OllamaUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
