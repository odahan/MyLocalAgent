using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Represents a direct invocation of a whitelisted Tool Map entry.
/// </summary>
public sealed class ToolStep : WorkflowStep
{
    internal ToolStep(WorkflowStepDefinition definition)
        : base(definition)
    {
    }
}
