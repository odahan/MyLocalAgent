namespace OD.MyLocalAgent.Core;

/// <summary>Defines the normalized Knowledge tags authorized for one execution surface.</summary>
public sealed record KnowledgeAccessDefinition
{
    /// <summary>Gets the tags that the owning task, workflow step, or conversation may search.</summary>
    public IReadOnlyList<string> AllowedTags { get; init; } = [];
}
