namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents a safe, content-free failure of the deterministic editorial workflow contract.
/// </summary>
internal sealed class EditorialContractException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EditorialContractException"/> class.
    /// </summary>
    /// <param name="message">The safe diagnostic message.</param>
    public EditorialContractException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EditorialContractException"/> class.
    /// </summary>
    /// <param name="message">The safe diagnostic message.</param>
    /// <param name="innerException">The underlying parsing exception retained for local diagnostics.</param>
    public EditorialContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
