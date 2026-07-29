namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Represents a failed Google Calendar API operation.
/// </summary>
public sealed class CalendarApiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CalendarApiException"/> class.
    /// </summary>
    /// <param name="operation">The failed connector operation.</param>
    /// <param name="statusCode">The HTTP status code, when available.</param>
    /// <param name="message">The provider error message.</param>
    /// <param name="innerException">The underlying API error.</param>
    public CalendarApiException(
        string operation,
        int? statusCode,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Operation = operation;
        StatusCode = statusCode;
    }

    /// <summary>
    /// Gets the failed connector operation.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Gets the HTTP status code, when available.
    /// </summary>
    public int? StatusCode { get; }
}
