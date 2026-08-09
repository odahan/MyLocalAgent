namespace OD.MyLocalAgent.Core;
/// <summary>Defines bounded read-only PDF operations.</summary>
public interface IPdfTool
{
    /// <summary>Reads non-content PDF information.</summary>
    Task<string> GetInfoAsync(string pdfPath, CancellationToken cancellationToken = default);
    /// <summary>Extracts bounded layout-aware PDF text.</summary>
    Task<string> ReadTextAsync(string pdfPath, int startPage = 1, int? maxPages = null, int? maxCharacters = null, CancellationToken cancellationToken = default);
}
