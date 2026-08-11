namespace OD.MyLocalAgent.Core;

/// <summary>
/// Contains deterministic tool, path, capability, and risk analysis for a workflow.
/// </summary>
/// <param name="RequiredToolGroups">All non-AI tool groups required by the workflow.</param>
/// <param name="MissingToolGroups">Required groups absent from the current task draft.</param>
/// <param name="MissingPathRequirements">Absolute-directory grants that are missing or insufficient.</param>
/// <param name="CapabilityRequirements">Referenced task capabilities and their current states.</param>
/// <param name="Warnings">Deterministic unresolved-path and execution-risk warnings.</param>
public sealed record WorkflowRequirementAnalysis(
    IReadOnlyList<string> RequiredToolGroups,
    IReadOnlyList<string> MissingToolGroups,
    IReadOnlyList<WorkflowPathRequirement> MissingPathRequirements,
    IReadOnlyList<WorkflowCapabilityRequirement> CapabilityRequirements,
    IReadOnlyList<string> Warnings);
