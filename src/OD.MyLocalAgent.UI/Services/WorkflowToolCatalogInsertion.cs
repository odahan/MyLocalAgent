namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Describes one deterministic insertion performed during a Tool catalog session.
/// </summary>
/// <param name="Alias">The inserted workflow alias.</param>
/// <param name="StepId">The generated step id.</param>
/// <param name="Position">The zero-based final top-level position.</param>
/// <param name="UsedFallback">Whether the verbatim fallback path was used.</param>
public sealed record WorkflowToolCatalogInsertion(
    string Alias,
    string StepId,
    int Position,
    bool UsedFallback);
