using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Contains the immutable, explicitly approved authoring result.
/// </summary>
/// <param name="WorkflowJson">The canonical indented workflow array.</param>
/// <param name="ToolGroups">The accepted union of task tool groups.</param>
/// <param name="FileSystemAccess">The accepted normalized filesystem rules.</param>
/// <param name="Capabilities">The explicitly accepted sensitive task permissions.</param>
public sealed record AIWorkflowHelperResult(
    string WorkflowJson,
    IReadOnlyList<string> ToolGroups,
    IReadOnlyList<FileSystemAccessRule> FileSystemAccess,
    IReadOnlyList<TaskCapability> Capabilities)
{
    /// <summary>Creates a result with no newly granted sensitive permissions.</summary>
    public AIWorkflowHelperResult(
        string workflowJson,
        IReadOnlyList<string> toolGroups,
        IReadOnlyList<FileSystemAccessRule> fileSystemAccess)
        : this(workflowJson, toolGroups, fileSystemAccess, [])
    {
    }
}
