namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Represents an invalid language-model provider configuration.
/// </summary>
public sealed class LlmConfigurationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LlmConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The actionable configuration error.</param>
    public LlmConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The actionable configuration error.</param>
    /// <param name="innerException">The underlying options error.</param>
    public LlmConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
