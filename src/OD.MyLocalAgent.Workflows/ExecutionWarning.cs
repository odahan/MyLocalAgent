namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Represents content-free non-failing execution warning metadata.
/// </summary>
/// <param name="Code">The stable program-owned warning code.</param>
/// <param name="Message">The content-free user-facing warning message.</param>
/// <param name="OccurrenceCount">The aggregate warning occurrence count.</param>
public sealed record ExecutionWarning(
    string Code,
    string Message,
    int OccurrenceCount);
