using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace OD.MyLocalAgent.Core;

internal static partial class HtmlDocumentConverter
{
    public static string Convert(string html, HtmlTextFormat format)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = new HtmlDocument();
        document.LoadHtml(html);
        foreach (var node in document.DocumentNode.SelectNodes(
                     "//script|//style|//noscript|//svg|//canvas|//form|//template|//iframe")
                 ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        var root = FirstNonEmpty(document.DocumentNode.SelectNodes("//main"))
            ?? FirstNonEmpty(document.DocumentNode.SelectNodes("//article"))
            ?? FirstNonEmpty(document.DocumentNode.SelectNodes("//body"))
            ?? document.DocumentNode;
        var builder = new StringBuilder();
        Append(root, builder, format, 0);
        return Normalize(builder.ToString(), format);
    }

    private static void Append(
        HtmlNode node,
        StringBuilder builder,
        HtmlTextFormat format,
        int listDepth)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            builder.Append(WebUtility.HtmlDecode(node.InnerText));
            return;
        }

        if (node.NodeType != HtmlNodeType.Element &&
            node.NodeType != HtmlNodeType.Document)
        {
            return;
        }

        var name = node.Name.ToLowerInvariant();
        if (format == HtmlTextFormat.Markdown && name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            AppendBreak(builder, 2);
            builder.Append('#', int.Parse(name.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(' ');
            AppendChildren(node, builder, format, listDepth);
            AppendBreak(builder, 2);
            return;
        }

        if (format == HtmlTextFormat.Markdown && name is "strong" or "b")
        {
            builder.Append("**");
            AppendChildren(node, builder, format, listDepth);
            builder.Append("**");
            return;
        }

        if (format == HtmlTextFormat.Markdown && name is "em" or "i")
        {
            builder.Append('*');
            AppendChildren(node, builder, format, listDepth);
            builder.Append('*');
            return;
        }

        if (format == HtmlTextFormat.Markdown && name == "a")
        {
            var label = NormalizeInline(WebUtility.HtmlDecode(node.InnerText));
            var href = node.GetAttributeValue("href", string.Empty).Trim();
            builder.Append(string.IsNullOrEmpty(href) ? label : $"[{label}]({href})");
            return;
        }

        if (format == HtmlTextFormat.Markdown && name == "code" && node.ParentNode?.Name != "pre")
        {
            builder.Append('`').Append(WebUtility.HtmlDecode(node.InnerText).Trim()).Append('`');
            return;
        }

        if (name == "pre")
        {
            AppendBreak(builder, 2);
            if (format == HtmlTextFormat.Markdown)
            {
                builder.Append("```\n");
            }

            builder.Append(WebUtility.HtmlDecode(node.InnerText).Trim());
            if (format == HtmlTextFormat.Markdown)
            {
                builder.Append("\n```");
            }

            AppendBreak(builder, 2);
            return;
        }

        if (name == "li")
        {
            AppendBreak(builder, 1);
            if (format == HtmlTextFormat.Markdown)
            {
                builder.Append(' ', Math.Max(0, listDepth - 1) * 2);
                var ordered = string.Equals(node.ParentNode?.Name, "ol", StringComparison.OrdinalIgnoreCase);
                builder.Append(ordered ? "1. " : "- ");
            }

            AppendChildren(node, builder, format, listDepth);
            AppendBreak(builder, 1);
            return;
        }

        if (format == HtmlTextFormat.Markdown && name == "blockquote")
        {
            AppendBreak(builder, 2);
            var quote = new StringBuilder();
            AppendChildren(node, quote, format, listDepth);
            foreach (var line in Normalize(quote.ToString(), format).Split('\n'))
            {
                builder.Append("> ").Append(line).Append('\n');
            }

            AppendBreak(builder, 1);
            return;
        }

        if (name == "br")
        {
            builder.Append('\n');
            return;
        }

        var isList = name is "ul" or "ol";
        var isBlock = IsBlock(name);
        if (isBlock)
        {
            AppendBreak(builder, format == HtmlTextFormat.Markdown ? 2 : 1);
        }

        AppendChildren(node, builder, format, isList ? listDepth + 1 : listDepth);
        if (isBlock)
        {
            AppendBreak(builder, format == HtmlTextFormat.Markdown ? 2 : 1);
        }
    }

    private static void AppendChildren(
        HtmlNode node,
        StringBuilder builder,
        HtmlTextFormat format,
        int listDepth)
    {
        foreach (var child in node.ChildNodes)
        {
            Append(child, builder, format, listDepth);
        }
    }

    private static bool IsBlock(string name) =>
        name is "address" or "article" or "aside" or "div" or "dl" or "dt" or "dd" or
            "footer" or "header" or "main" or "nav" or "p" or "section" or "table" or
            "thead" or "tbody" or "tfoot" or "tr";

    private static HtmlNode? FirstNonEmpty(HtmlNodeCollection? nodes) =>
        nodes?.FirstOrDefault(static node => !string.IsNullOrWhiteSpace(node.InnerText));

    private static void AppendBreak(StringBuilder builder, int count)
    {
        var existing = 0;
        for (var index = builder.Length - 1; index >= 0 && builder[index] == '\n'; index--)
        {
            existing++;
        }

        builder.Append('\n', Math.Max(0, count - existing));
    }

    private static string Normalize(string value, HtmlTextFormat format)
    {
        value = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = value.Split('\n')
            .Select(static line => InlineWhitespacePattern().Replace(line, " ").Trim())
            .ToList();
        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var maximumBlankLines = format == HtmlTextFormat.Markdown ? 1 : 0;
        var normalized = new List<string>(lines.Count);
        var blankLines = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blankLines++;
                if (blankLines > maximumBlankLines)
                {
                    continue;
                }
            }
            else
            {
                blankLines = 0;
            }

            normalized.Add(line);
        }

        return string.Join('\n', normalized);
    }

    private static string NormalizeInline(string value) =>
        InlineWhitespacePattern().Replace(value, " ").Trim();

    [GeneratedRegex(@"[\s\p{Z}]+", RegexOptions.CultureInvariant)]
    private static partial Regex InlineWhitespacePattern();
}
