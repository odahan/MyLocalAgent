namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Describes the AI Workflow Helper operation state.
/// </summary>
public enum AIWorkflowHelperState
{
    /// <summary>
    /// No request is active.
    /// </summary>
    Idle,

    /// <summary>
    /// A provider response is streaming.
    /// </summary>
    Generating,

    /// <summary>
    /// The application is validating a completed response.
    /// </summary>
    Validating,

    /// <summary>
    /// A valid candidate needs explicit proposal approval.
    /// </summary>
    NeedsApproval,

    /// <summary>
    /// The result is valid and ready.
    /// </summary>
    Ready,

    /// <summary>
    /// The request exhausted its attempts or failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The user cancelled streaming.
    /// </summary>
    Cancelled,
}
