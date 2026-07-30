using OD.MyLocalAgent.Ollama;
using Microsoft.Extensions.Options;

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
    /// <param name="aiOptions">The optional reloadable global generation defaults.</param>
    public ExtractActionItemsSkill(
        ILlmClientFactory clientFactory,
        IOptionsMonitor<AIOptions>? aiOptions = null)
        : base(clientFactory, aiOptions)
    {
    }

    /// <inheritdoc />
    public override string Name => "extractActionItems";

    /// <inheritdoc />
    public override string AgentFunctionName => "skill_extract_action_items";

    /// <inheritdoc />
    public override string Description =>
        "Extracts explicit action items from one or more untrusted messages, including stated owners, due dates, and sources.";

    /// <inheritdoc />
    protected override string Instruction =>
        "Extract only explicit, concrete action items from one or more messages. " +
        "Return Markdown checklist rows in the form '- [ ] Action — Owner: value — Due: value — Source: value'. " +
        "Use 'Not stated' for missing owner, due date, or source; never infer or invent them. " +
        "If there are no explicit action items, return exactly 'No explicit action items.'";
}
