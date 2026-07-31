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
    private readonly IThemeService? _themeService;

    /// <summary>
    /// Initializes a renderer that follows the current application theme when available.
    /// </summary>
    /// <param name="themeService">The optional live application theme source.</param>
    public MarkdownRenderingService(IThemeService? themeService = null)
    {
        _themeService = themeService;
    }

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
        var isDark = _themeService?.CurrentTheme != AppTheme.Light;
        var colorScheme = isDark ? "dark" : "light";
        var background = isDark ? "#1e1e24" : "#fafafa";
        var foreground = isDark ? "#f1f1f4" : "#202124";
        var accent = isDark ? "#8ab4f8" : "#365f9d";
        var codeBackground = isDark ? "#292932" : "#eeeeF2";
        var border = isDark ? "#555" : "#b8b8c0";
        var quote = isDark ? "#c8c8cf" : "#53535c";
        return
            $$"""
            <!doctype html>
            <html><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'none'; connect-src 'none'; frame-src 'none'; child-src 'none'; img-src 'none'; media-src 'none'; font-src 'none'; object-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
            <style>
            :root{color-scheme:{{colorScheme}}}body{margin:0;padding:20px;background:{{background}};color:{{foreground}};font:14px/1.55 Segoe UI,Arial,sans-serif}
            h1,h2,h3{color:{{accent}}}code,pre{font-family:Consolas,monospace;background:{{codeBackground}}}pre{padding:12px;overflow:auto}
            a{color:{{accent}}}table{border-collapse:collapse}th,td{border:1px solid {{border}};padding:6px 9px}blockquote{border-left:3px solid {{border}};margin-left:0;padding-left:12px;color:{{quote}}}
            </style></head><body>
            """ +
            body +
            "</body></html>";
    }
}
