namespace OD.MyLocalAgent.Core;

/// <summary>
/// Indicates that an agent repeated the same tool call often enough to trigger loop protection.
/// </summary>
public sealed class AgentToolLoopException : Exception
{
    /// <summary>
    /// Initializes a repeated-tool-call failure.
    /// </summary>
    /// <param name="message">The safe diagnostic message.</param>
    public AgentToolLoopException(string message)
        : base(message)
    {
    }
}
