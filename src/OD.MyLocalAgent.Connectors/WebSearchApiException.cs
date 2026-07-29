namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Represents a failed web-search provider operation.
/// </summary>
public sealed class WebSearchApiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebSearchApiException"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code, when available.</param>
    /// <param name="message">The provider error message.</param>
    /// <param name="innerException">The underlying transport or protocol error, when available.</param>
    public WebSearchApiException(
        int? statusCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Gets the HTTP status code, when available.
    /// </summary>
    public int? StatusCode { get; }
}
