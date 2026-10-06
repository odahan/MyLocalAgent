namespace OD.MyLocalAgent.Core;

/// <summary>Identifies the authorized caller rather than inferring it from a task identifier.</summary>
public enum ToolInvocationSurface
{
    /// <summary>An agent, Direct Chat, or ordinary interactive tool test.</summary>
    Agent,
    /// <summary>A validated scheduled workflow.</summary>
    Workflow,
    /// <summary>An explicitly authorized workflow test.</summary>
    WorkflowTest,
}
