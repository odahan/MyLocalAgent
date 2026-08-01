namespace OD.MyLocalAgent.Core;

/// <summary>
/// Identifies the origin stream of one run-log line.
/// </summary>
public enum RunLogStream
{
    /// <summary>
    /// The runner standard output stream.
    /// </summary>
    Output,

    /// <summary>
    /// The runner standard error stream.
    /// </summary>
    Error,
}
