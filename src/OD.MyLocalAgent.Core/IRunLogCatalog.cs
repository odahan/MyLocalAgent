namespace OD.MyLocalAgent.Core;

/// <summary>
/// Reads persisted run-log files for the run monitor (§19.4).
/// </summary>
public interface IRunLogCatalog
{
    /// <summary>
    /// Lists all persisted runs, newest first.
    /// </summary>
    /// <returns>The run descriptors.</returns>
    IReadOnlyList<RunLogRunInfo> GetRuns();

    /// <summary>
    /// Reads the complete lines appended after the supplied offset.
    /// </summary>
    /// <param name="filePath">The run-log file path.</param>
    /// <param name="offset">The byte offset returned by the previous read, or zero.</param>
    /// <returns>The decoded lines and the next offset.</returns>
    RunLogReadResult ReadLines(string filePath, long offset);
}
