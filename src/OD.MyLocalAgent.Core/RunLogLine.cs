namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents one recorded runner output line.
/// </summary>
/// <param name="TimestampUtc">The UTC moment the line was written.</param>
/// <param name="Stream">The origin stream.</param>
/// <param name="Text">The line text without its terminator.</param>
public sealed record RunLogLine(
    DateTimeOffset TimestampUtc,
    RunLogStream Stream,
    string Text);
