namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Describes one already-minimized external-content item for bounded processing.
/// </summary>
/// <param name="Id">The opaque correlation identifier.</param>
/// <param name="Content">The compact external content.</param>
/// <param name="MaximumSummaryCharacters">The maximum generated summary length.</param>
public sealed record CompactExternalContentItem(
    string Id,
    string Content,
    int MaximumSummaryCharacters);
