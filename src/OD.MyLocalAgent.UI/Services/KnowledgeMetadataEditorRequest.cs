namespace OD.MyLocalAgent.UI.Services;

/// <summary>Captures the editable metadata of one existing Knowledge document.</summary>
/// <param name="Name">The current document name.</param>
/// <param name="Description">The current document description.</param>
/// <param name="Tags">The current normalized authorization tags.</param>
public sealed record KnowledgeMetadataEditorRequest(
    string Name,
    string Description,
    IReadOnlyList<string> Tags);
