using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Represents an invocable Tool Map entry in the direct tester.
/// </summary>
/// <param name="Alias">The workflow alias.</param>
/// <param name="Group">The owning tool group.</param>
/// <param name="Arguments">The ordered JSON argument names.</param>
/// <param name="OptionalArguments">Arguments that may be omitted.</param>
/// <param name="RequiredCapability">The optional task capability required by the operation.</param>
public sealed record ToolAliasItem(
    string Alias,
    string Group,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> OptionalArguments,
    TaskCapability? RequiredCapability);
