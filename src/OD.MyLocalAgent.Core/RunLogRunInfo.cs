namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes one persisted run-log file.
/// </summary>
/// <param name="FilePath">The absolute run-log file path.</param>
/// <param name="TaskId">The executed task id.</param>
/// <param name="StartedUtc">The UTC execution start.</param>
/// <param name="EndedUtc">The UTC execution end, or <see langword="null"/> while in progress.</param>
/// <param name="ExitCode">The runner exit code, or <see langword="null"/> while in progress.</param>
public sealed record RunLogRunInfo(
    string FilePath,
    string TaskId,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc,
    int? ExitCode)
{
    /// <summary>Gets whether the completed run surfaced execution warnings.</summary>
    public bool HasWarnings { get; init; }
}
