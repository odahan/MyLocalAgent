namespace OD.MyLocalAgent.UI.Services;

/// <summary>Displays the modal metadata editor for an existing Knowledge document.</summary>
public interface IKnowledgeMetadataEditorDialogService
{
    /// <summary>Shows the editor and returns the applied metadata, or null when cancelled.</summary>
    /// <param name="request">The current metadata snapshot.</param>
    KnowledgeMetadataEditorResult? Show(KnowledgeMetadataEditorRequest request);
}
