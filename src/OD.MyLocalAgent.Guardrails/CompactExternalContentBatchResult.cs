namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Carries ordered compact item results and content-free phase failures.
/// </summary>
/// <param name="Items">Exactly one result for each input item in caller order.</param>
/// <param name="Failures">The failed batch phases.</param>
public sealed record CompactExternalContentBatchResult(
    IReadOnlyList<CompactExternalContentResult> Items,
    IReadOnlyList<CompactExternalContentBatchFailure> Failures);
