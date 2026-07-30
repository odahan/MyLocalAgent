namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Projects one Markdown response to deterministic text and restricted local HTML.
/// </summary>
public interface IMarkdownRenderingService
{
    /// <summary>
    /// Converts Markdown to deterministic plain text.
    /// </summary>
    /// <param name="markdown">The Markdown source.</param>
    /// <returns>The plain-text projection.</returns>
    string ToPlainText(string markdown);

    /// <summary>
    /// Converts Markdown to a complete restricted in-memory HTML document.
    /// </summary>
    /// <param name="markdown">The Markdown source.</param>
    /// <returns>The restricted HTML document.</returns>
    string ToRestrictedHtml(string markdown);
}
