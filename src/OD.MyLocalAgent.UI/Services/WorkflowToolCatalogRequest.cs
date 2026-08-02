using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Captures the current task draft supplied to the memory-only Tool catalog dialog.
/// </summary>
/// <param name="WorkflowText">The current workflow editor text.</param>
/// <param name="CaretIndex">The current UTF-16 workflow editor caret index.</param>
/// <param name="ToolGroups">The current non-AI task tool groups.</param>
/// <param name="FileSystemAccess">The current task authorization rules, shown as context only.</param>
/// <param name="Safety">The current global Safety snapshot, shown but never changed.</param>
/// <param name="ErrorPolicy">The current task error policy.</param>
/// <param name="Temperature">The effective task Temperature.</param>
/// <param name="TopP">The effective task Top P.</param>
public sealed record WorkflowToolCatalogRequest(
    string WorkflowText,
    int CaretIndex,
    IReadOnlyList<string> ToolGroups,
    IReadOnlyList<FileSystemAccessRule> FileSystemAccess,
    SafetyOptions Safety,
    WorkflowErrorPolicy ErrorPolicy,
    double Temperature,
    double TopP);
