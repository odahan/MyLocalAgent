namespace OD.MyLocalAgent.Core;
/// <summary>Defines read-only DOCX text extraction.</summary>
public interface IDocumentTool
{
    /// <summary>Reads bounded DOCX body text.</summary>
    Task<string> ReadDocxAsync(string documentPath, int? maxCharacters = null, CancellationToken cancellationToken = default);
}
