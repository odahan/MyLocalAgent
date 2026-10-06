namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents one incremental run-log read.
/// </summary>
/// <param name="Lines">The complete lines decoded since the supplied offset.</param>
/// <param name="NewOffset">The offset to supply to the next read.</param>
/// <param name="EndedUtc">The UTC execution end when the end record was reached.</param>
/// <param name="ExitCode">The exit code when the end record was reached.</param>
public sealed record RunLogReadResult(
    IReadOnlyList<RunLogLine> Lines,
    long NewOffset,
    DateTimeOffset? EndedUtc,
    int? ExitCode)
{
    /// <summary>Gets whether the end record reports execution warnings.</summary>
    public bool HasWarnings { get; init; }
}
