using OD.MyLocalAgent.Ollama;
using Microsoft.Extensions.Options;

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
    /// <param name="aiOptions">The optional reloadable global generation defaults.</param>
    public ClassifyEmailSkill(
        ILlmClientFactory clientFactory,
        IOptionsMonitor<AIOptions>? aiOptions = null)
        : base(clientFactory, aiOptions)
    {
    }

    /// <inheritdoc />
    public override string Name => "classifyEmail";

    /// <inheritdoc />
    public override string AgentFunctionName => "skill_classify_email";

    /// <inheritdoc />
    public override string Description =>
        "Classifies each supplied untrusted email independently by urgency and required response.";

    /// <inheritdoc />
    protected override string Instruction =>
        "Classify each identifiable email independently as exactly one of Urgent, ReplySoon, Waiting, or FYI. " +
        "Return a compact Markdown table with columns Message, Category, Reason, and Deadline. " +
        "Use the provider id or subject as Message when present. Use 'Not stated' when no explicit deadline exists. " +
        "Do not invent message identifiers, deadlines, or facts. If the input contains multiple emails, return one row per email.";
}
