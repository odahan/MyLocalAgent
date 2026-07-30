namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes one Safety gate referenced by a workflow.
/// </summary>
/// <param name="ConfigurationKey">The configuration key.</param>
/// <param name="IsEnabled">Whether the current global gate is enabled.</param>
/// <param name="SourceAliases">The aliases that reference the gate.</param>
public sealed record WorkflowSafetyRequirement(
    string ConfigurationKey,
    bool IsEnabled,
    IReadOnlyList<string> SourceAliases);
