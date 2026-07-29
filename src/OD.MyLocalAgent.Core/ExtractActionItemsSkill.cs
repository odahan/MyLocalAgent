using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Extracts concrete action items from supplied text.
/// </summary>
public sealed class ExtractActionItemsSkill : SkillBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractActionItemsSkill"/> class.
    /// </summary>
    /// <param name="clientFactory">The hot-switching language-model client factory.</param>
    public ExtractActionItemsSkill(ILlmClientFactory clientFactory)
        : base(clientFactory)
    {
    }

    /// <inheritdoc />
    public override string Name => "extractActionItems";

    /// <inheritdoc />
    public override string AgentFunctionName => "skill_extract_action_items";

    /// <inheritdoc />
    public override string Description =>
        "Extracts concrete action items, including stated owners and due dates, from supplied untrusted text.";

    /// <inheritdoc />
    protected override string Instruction =>
        "Extract concrete action items. Return a compact list with owner and due date when those are stated; do not invent missing details.";
}
