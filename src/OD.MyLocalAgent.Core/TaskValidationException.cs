namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents an invalid scheduled-task definition.
/// </summary>
public sealed class TaskValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TaskValidationException"/> class.
    /// </summary>
    /// <param name="message">The validation message.</param>
    public TaskValidationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskValidationException"/> class.
    /// </summary>
    /// <param name="message">The validation message.</param>
    /// <param name="innerException">The underlying validation error.</param>
    public TaskValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
