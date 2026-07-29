using OD.MyLocalAgent.Ollama;

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
    public SummarizeTextSkill(ILlmClientFactory clientFactory)
        : base(clientFactory)
    {
    }

    /// <inheritdoc />
    public override string Name => "summarize";

    /// <inheritdoc />
    protected override string Instruction =>
        "Summarize the supplied content concisely. Preserve important facts, decisions, dates, and action owners.";
}
