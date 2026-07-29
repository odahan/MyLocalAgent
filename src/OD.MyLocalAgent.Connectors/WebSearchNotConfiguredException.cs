namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Represents an intentionally disabled web-search connector.
/// </summary>
public sealed class WebSearchNotConfiguredException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebSearchNotConfiguredException"/> class.
    /// </summary>
    /// <param name="message">The actionable configuration message.</param>
    public WebSearchNotConfiguredException(string message)
        : base(message)
    {
    }
}
