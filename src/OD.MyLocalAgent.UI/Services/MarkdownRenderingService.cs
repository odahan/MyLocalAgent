using Markdig;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Uses Markdig with raw HTML disabled and wraps output in a restrictive local document.
/// </summary>
public sealed class MarkdownRenderingService : IMarkdownRenderingService
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAdvancedExtensions()
            .Build();

    /// <inheritdoc />
    public string ToPlainText(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Markdown.ToPlainText(markdown, Pipeline).Trim();
    }

    /// <inheritdoc />
    public string ToRestrictedHtml(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var body = Markdown.ToHtml(markdown, Pipeline);
        return
            """
            <!doctype html>
            <html><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'none'; connect-src 'none'; frame-src 'none'; child-src 'none'; img-src 'none'; media-src 'none'; font-src 'none'; object-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
            <style>
            :root{color-scheme:dark}body{margin:0;padding:20px;background:#1e1e24;color:#f1f1f4;font:14px/1.55 Segoe UI,Arial,sans-serif}
            h1,h2,h3{color:#8ab4f8}code,pre{font-family:Consolas,monospace;background:#292932}pre{padding:12px;overflow:auto}
            a{color:#8ab4f8}table{border-collapse:collapse}th,td{border:1px solid #555;padding:6px 9px}blockquote{border-left:3px solid #777;margin-left:0;padding-left:12px;color:#c8c8cf}
            </style></head><body>
            """ +
            body +
            "</body></html>";
    }
}
