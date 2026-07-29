using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Represents an agent prompt step with the task's whitelisted tools.
/// </summary>
public sealed class PromptStep : WorkflowStep
{
    internal PromptStep(WorkflowStepDefinition definition)
        : base(definition)
    {
    }
}
