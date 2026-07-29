namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents invalid workflow syntax or references.
/// </summary>
public sealed class WorkflowValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowValidationException"/> class.
    /// </summary>
    /// <param name="message">The validation message.</param>
    public WorkflowValidationException(string message)
        : base(message)
    {
    }
}
