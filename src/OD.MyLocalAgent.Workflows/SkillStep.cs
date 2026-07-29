using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Represents an invocation of a named AI skill.
/// </summary>
public sealed class SkillStep : WorkflowStep
{
    internal SkillStep(WorkflowStepDefinition definition, string skillName)
        : base(definition)
    {
        SkillName = skillName;
    }

    /// <summary>
    /// Gets the registered Core skill name.
    /// </summary>
    public string SkillName { get; }
}
