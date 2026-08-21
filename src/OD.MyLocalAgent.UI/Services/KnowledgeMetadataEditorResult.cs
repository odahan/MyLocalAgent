namespace OD.MyLocalAgent.UI.Services;

/// <summary>Describes the validated metadata applied by the editor.</summary>
/// <param name="Name">The updated document name.</param>
/// <param name="Description">The updated document description.</param>
/// <param name="Tags">The updated normalized authorization tags.</param>
public sealed record KnowledgeMetadataEditorResult(
    string Name,
    string Description,
    IReadOnlyList<string> Tags);
