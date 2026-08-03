namespace OD.MyLocalAgent.Core;

/// <summary>
/// Identifies why an agent-prompt placeholder occurrence was left unchanged.
/// </summary>
public enum AgentPromptPlaceholderWarningKind
{
    /// <summary>
    /// The complete marker has no prompt-only definition.
    /// </summary>
    Unknown,

    /// <summary>
    /// The marker is available only while executing a workflow.
    /// </summary>
    WorkflowOnly,

    /// <summary>
    /// A Today or Now marker contains an invalid custom format.
    /// </summary>
    InvalidDateTimeFormat,

    /// <summary>
    /// The source contains malformed double-brace placeholder syntax.
    /// </summary>
    MalformedBraces,
}
