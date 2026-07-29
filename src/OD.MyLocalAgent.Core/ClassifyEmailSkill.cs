using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Classifies email content by urgency and required response.
/// </summary>
public sealed class ClassifyEmailSkill : SkillBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClassifyEmailSkill"/> class.
    /// </summary>
    /// <param name="clientFactory">The hot-switching language-model client factory.</param>
    public ClassifyEmailSkill(ILlmClientFactory clientFactory)
        : base(clientFactory)
    {
    }

    /// <inheritdoc />
    public override string Name => "classifyEmail";

    /// <inheritdoc />
    public override string AgentFunctionName => "skill_classify_email";

    /// <inheritdoc />
    public override string Description =>
        "Classifies supplied untrusted email content by urgency and required response.";

    /// <inheritdoc />
    protected override string Instruction =>
        "Classify the email as Urgent, ReplySoon, Waiting, or FYI. Give one short reason and identify any explicit deadline.";
}
