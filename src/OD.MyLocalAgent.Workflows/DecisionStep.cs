using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>Represents a deterministic workflow routing decision.</summary>
public sealed class DecisionStep : WorkflowStep
{
    /// <summary>Initializes a decision step.</summary>
    public DecisionStep(WorkflowStepDefinition definition)
        : base(definition)
    {
    }
}
