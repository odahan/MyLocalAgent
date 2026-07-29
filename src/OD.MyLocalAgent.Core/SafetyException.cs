namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents an operation blocked by an opt-in safety gate.
/// </summary>
public sealed class SafetyException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SafetyException"/> class.
    /// </summary>
    /// <param name="operation">The blocked operation.</param>
    /// <param name="requiredFlag">The configuration flag required to allow the operation.</param>
    public SafetyException(string operation, string requiredFlag)
        : base($"Operation '{operation}' is disabled. Enable '{requiredFlag}' to allow it.")
    {
        Operation = operation;
        RequiredFlag = requiredFlag;
    }

    /// <summary>
    /// Gets the blocked operation.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Gets the required configuration flag.
    /// </summary>
    public string RequiredFlag { get; }
}
