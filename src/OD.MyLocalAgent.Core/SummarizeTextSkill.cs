using OD.MyLocalAgent.Ollama;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Produces a concise factual summary of supplied text.
/// </summary>
public sealed class SummarizeTextSkill : SkillBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SummarizeTextSkill"/> class.
    /// </summary>
    /// <param name="clientFactory">The hot-switching language-model client factory.</param>
    /// <param name="aiOptions">The optional reloadable global generation defaults.</param>
    public SummarizeTextSkill(
        ILlmClientFactory clientFactory,
        IOptionsMonitor<AIOptions>? aiOptions = null)
        : base(clientFactory, aiOptions)
    {
    }

    /// <inheritdoc />
    public override string Name => "summarize";

    /// <inheritdoc />
    public override string AgentFunctionName => "skill_summarize";

    /// <inheritdoc />
    public override string Description =>
        "Summarizes supplied untrusted text while preserving important facts, decisions, dates, and action owners.";

    /// <inheritdoc />
    protected override string Instruction =>
        "Summarize the supplied content concisely. Preserve important facts, decisions, dates, and action owners.";
}
