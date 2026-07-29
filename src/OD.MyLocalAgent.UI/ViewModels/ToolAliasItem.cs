namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Represents an invocable Tool Map entry in the direct tester.
/// </summary>
/// <param name="Alias">The workflow alias.</param>
/// <param name="Group">The owning tool group.</param>
/// <param name="Arguments">The ordered JSON argument names.</param>
/// <param name="SafetyFlag">The optional safety flag required by the operation.</param>
public sealed record ToolAliasItem(
    string Alias,
    string Group,
    IReadOnlyList<string> Arguments,
    string? SafetyFlag);
