using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Creates bounded media-aware HTTP text projections without network access.
/// </summary>
public static partial class HttpContentProjector
{
    private const string UnsupportedMarker =
        "[HTTP CONTENT OMITTED: the response media type is unsupported or binary.]";

    /// <summary>
    /// Projects fetched HTTP content according to its media type.
    /// </summary>
    /// <param name="content">The fetched capped response text.</param>
    /// <param name="mediaType">The response media type.</param>
    /// <param name="maximumCharacters">The Guardrails projection bound.</param>
    /// <returns>The bounded projection and aggregate metadata.</returns>
    public static HttpContentProjection Project(
        string content,
        string? mediaType,
        int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (maximumCharacters < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        }

        var normalizedMediaType = mediaType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        string projection;
        var unsupported = false;
        if (normalizedMediaType is "text/html" or "application/xhtml+xml")
        {
            projection = ExtractReadableHtml(content);
        }
        else if (normalizedMediaType is "application/json" or "text/json" ||
                 normalizedMediaType?.EndsWith("+json", StringComparison.Ordinal) == true)
        {
            projection = MinifyJsonOrNormalize(content);
        }
        else if (normalizedMediaType is null ||
                 normalizedMediaType.StartsWith("text/", StringComparison.Ordinal) ||
                 normalizedMediaType is "application/xml" or "application/rss+xml" or
                     "application/atom+xml" ||
                 normalizedMediaType.EndsWith("+xml", StringComparison.Ordinal))
        {
            projection = NormalizeText(content);
        }
        else
        {
            projection = UnsupportedMarker;
            unsupported = true;
        }

        var capped = Cap(projection, maximumCharacters);
        var omitted = unsupported
            ? content.Length
            : Math.Max(capped.Omitted, Math.Max(0, content.Length - capped.Value.Length));
        return new HttpContentProjection(
            capped.Value,
            content.Length,
            capped.Value.Length,
            omitted,
            capped.Omitted > 0);
    }

    private static string MinifyJsonOrNormalize(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            return NormalizeText(content);
        }
    }

    private static string ExtractReadableHtml(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        foreach (var node in document.DocumentNode.SelectNodes(
                     "//script|//style|//noscript|//svg|//canvas|//form|//nav|//header|//footer|//aside")
                 ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        var root = FirstNonEmpty(document.DocumentNode.SelectNodes("//main"))
            ?? FirstNonEmpty(document.DocumentNode.SelectNodes("//article"))
            ?? FirstNonEmpty(document.DocumentNode.SelectNodes("//body"))
            ?? document.DocumentNode;
        var blocks = new List<string>();
        var title = document.DocumentNode.SelectSingleNode("//title");
        if (title is not null)
        {
            blocks.Add(CompactInline(WebUtility.HtmlDecode(title.InnerText)));
        }

        var description = document.DocumentNode.SelectSingleNode(
            "//meta[translate(@name,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='description']");
        if (description is not null)
        {
            blocks.Add(CompactInline(WebUtility.HtmlDecode(description.GetAttributeValue("content", string.Empty))));
        }

        blocks.AddRange(root.DescendantsAndSelf()
            .Where(static node => node.NodeType == HtmlNodeType.Element && IsUsefulBlock(node.Name))
            .Select(static node => CompactInline(WebUtility.HtmlDecode(node.InnerText)))
            .Where(static text => text.Length > 0));
        if (blocks.Count == 0)
        {
            blocks.Add(CompactInline(WebUtility.HtmlDecode(root.InnerText)));
        }

        return string.Join('\n', blocks.Where(static block => block.Length > 0));
    }

    private static HtmlNode? FirstNonEmpty(HtmlNodeCollection? nodes) =>
        nodes?.FirstOrDefault(
            static node => !string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(node.InnerText)));

    private static bool IsUsefulBlock(string name) =>
        name is "title" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or
            "p" or "li" or "td" or "th" or "blockquote" or "pre";

    private static string CompactInline(string value) =>
        InlineWhitespacePattern().Replace(NormalizeText(value), " ").Trim();

    private static string NormalizeText(string value)
    {
        value = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var normalized = new string(value.Select(
            static character => character == '\n' || !char.IsControl(character)
                ? character
                : ' ').ToArray());
        return string.Join(
            '\n',
            normalized.Split('\n').Select(static line => line.TrimEnd(' ', '\t')));
    }

    private static CappedProjection Cap(string value, int maximum)
    {
        if (value.Length <= maximum)
        {
            return new CappedProjection(value, 0);
        }

        var omitted = value.Length;
        while (true)
        {
            var marker = $"[... {omitted.ToString(CultureInfo.InvariantCulture)} characters omitted ...]";
            if (marker.Length >= maximum)
            {
                return new CappedProjection(marker[..maximum], value.Length);
            }

            var prefixLength = maximum - marker.Length;
            var updated = value.Length - prefixLength;
            if (updated == omitted)
            {
                return new CappedProjection(value[..prefixLength] + marker, omitted);
            }

            omitted = updated;
        }
    }

    [GeneratedRegex(@"[\s\p{Z}]+", RegexOptions.CultureInvariant)]
    private static partial Regex InlineWhitespacePattern();

    private readonly record struct CappedProjection(string Value, int Omitted);
}
