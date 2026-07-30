using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Contains the immutable, explicitly approved authoring result.
/// </summary>
/// <param name="WorkflowJson">The canonical indented workflow array.</param>
/// <param name="ToolGroups">The accepted union of task tool groups.</param>
/// <param name="FileSystemAccess">The accepted normalized filesystem rules.</param>
public sealed record AIWorkflowHelperResult(
    string WorkflowJson,
    IReadOnlyList<string> ToolGroups,
    IReadOnlyList<FileSystemAccessRule> FileSystemAccess);
