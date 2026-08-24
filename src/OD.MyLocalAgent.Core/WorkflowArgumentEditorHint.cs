namespace OD.MyLocalAgent.Core;

/// <summary>Provides a small semantic override for a generated workflow argument editor.</summary>
public enum WorkflowArgumentEditorHint
{
    /// <summary>Uses the editor inferred from the argument kind and metadata.</summary>
    Automatic,
    /// <summary>Uses a structured JSON editor for an object or array value.</summary>
    StructuredJson,
    /// <summary>Uses the task-bounded Knowledge tag selection editor.</summary>
    KnowledgeTags,
}
