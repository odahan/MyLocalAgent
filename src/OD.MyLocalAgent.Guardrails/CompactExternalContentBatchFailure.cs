namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Describes one content-free failed compact-processing phase.
/// </summary>
/// <param name="Phase">The program-owned phase name.</param>
/// <param name="Exception">The sanitized exception safe for incident reporting.</param>
/// <param name="ItemCount">The aggregate number of affected items.</param>
public sealed record CompactExternalContentBatchFailure(
    string Phase,
    Exception Exception,
    int ItemCount);
