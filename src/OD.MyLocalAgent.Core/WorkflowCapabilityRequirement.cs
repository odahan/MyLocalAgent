namespace OD.MyLocalAgent.Core;

/// <summary>Describes one sensitive task capability referenced by a workflow.</summary>
public sealed record WorkflowCapabilityRequirement(
    TaskCapability Capability,
    bool IsGranted,
    IReadOnlyList<string> SourceAliases);
