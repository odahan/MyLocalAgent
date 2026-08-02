namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Identifies one modified prompt step without exposing prompt content.
/// </summary>
/// <param name="Position">The zero-based workflow position.</param>
/// <param name="EffectiveId">The effective step id.</param>
public sealed record WorkflowPromptEditorChange(int Position, string EffectiveId);
