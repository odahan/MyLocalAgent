using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>Creates deterministic, bounded chunks from extracted text.</summary>
public sealed partial class KnowledgeChunker
{
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IOptionsMonitor<KnowledgeOptions> _options;

    /// <summary>Initializes the deterministic chunker.</summary>
    public KnowledgeChunker(ITokenEstimator tokenEstimator, IOptionsMonitor<KnowledgeOptions> options)
    {
        _tokenEstimator = tokenEstimator;
        _options = options;
    }

    /// <summary>Splits normalized extracted text into stable chunks.</summary>
    public IReadOnlyList<KnowledgeChunk> Chunk(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var options = _options.CurrentValue;
        var targetCharacters = checked(options.TargetChunkTokens * 4);
        var maximumCharacters = checked(options.MaximumChunkTokens * 4);
        var overlapCharacters = checked(options.OverlapChunkTokens * 4);
        var chunks = new List<KnowledgeChunk>();
        var start = 0;
        while (start < text.Length && chunks.Count < options.MaximumChunksPerDocument)
        {
            var preferredEnd = Math.Min(text.Length, start + targetCharacters);
            var proposedEnd = Math.Min(text.Length, start + maximumCharacters);
            var end = proposedEnd == text.Length
                ? proposedEnd
                : FindBoundary(text, start, preferredEnd, proposedEnd);
            if (end <= start)
            {
                end = proposedEnd;
            }

            end = ConstrainToTokenLimit(text, start, end, options.MaximumChunkTokens);

            var value = text[start..end].Trim();
            if (value.Length > 0 && (chunks.Count == 0 || !string.Equals(chunks[^1].Text, value, StringComparison.Ordinal)))
            {
                var ordinal = chunks.Count;
                chunks.Add(new KnowledgeChunk(
                    $"{ordinal:D8}", ordinal, value, _tokenEstimator.EstimateText(value),
                    start, end, FindHeading(text, start), null));
            }

            if (end == text.Length)
            {
                start = text.Length;
                break;
            }

            start = FindOverlapStart(text, start, end, overlapCharacters);
        }

        if (start < text.Length)
        {
            throw new InvalidOperationException("The document exceeds the configured chunk limit.");
        }

        return chunks;
    }

    /// <summary>Splits structured extracted text while retaining source metadata.</summary>
    public IReadOnlyList<KnowledgeChunk> Chunk(KnowledgeExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var chunks = Chunk(document.Text).ToArray();
        for (var index = 0; index < chunks.Length; index++)
        {
            var chunk = chunks[index];
            var block = document.Blocks.FirstOrDefault(candidate =>
                candidate.StartCharacter <= chunk.StartCharacter &&
                candidate.EndCharacter > chunk.StartCharacter);
            if (block is not null)
            {
                chunks[index] = chunk with
                {
                    Heading = chunk.Heading ?? block.Heading,
                    PageNumber = block.PageNumber,
                };
            }
        }

        return chunks;
    }

    private int ConstrainToTokenLimit(string text, int start, int end, int maximumTokens)
    {
        if (_tokenEstimator.EstimateText(text[start..end].Trim()) <= maximumTokens)
        {
            return end;
        }

        var low = start + 1;
        var high = end;
        var best = start;
        while (low <= high)
        {
            var candidate = low + ((high - low) / 2);
            var tokens = _tokenEstimator.EstimateText(text[start..candidate].Trim());
            if (tokens <= maximumTokens)
            {
                best = candidate;
                low = candidate + 1;
            }
            else
            {
                high = candidate - 1;
            }
        }

        if (best == start)
        {
            throw new InvalidOperationException(
                "The configured Knowledge chunk token limit cannot contain the extracted text.");
        }

        return best;
    }

    private static int FindBoundary(string text, int start, int preferredEnd, int maximumEnd)
    {
        var minimum = start + ((preferredEnd - start) / 2);
        for (var index = preferredEnd - 1; index >= minimum; index--)
        {
            if (text[index] == '\n' || (text[index] is '.' or '!' or '?' && index + 1 < text.Length && char.IsWhiteSpace(text[index + 1])))
            {
                return index + 1;
            }
        }

        for (var index = preferredEnd; index < maximumEnd; index++)
        {
            if (text[index] == '\n' || (text[index] is '.' or '!' or '?' && index + 1 < text.Length && char.IsWhiteSpace(text[index + 1])))
            {
                return index + 1;
            }
        }

        return maximumEnd;
    }

    private static string? FindHeading(string text, int start)
    {
        var prefix = text[..start];
        var matches = MarkdownHeading().Matches(prefix);
        return matches.Count == 0 ? null : matches[^1].Groups[1].Value.Trim();
    }

    private static int FindOverlapStart(string text, int previousStart, int end, int overlapCharacters)
    {
        var nextStart = Math.Max(previousStart + 1, end - overlapCharacters);
        foreach (Match heading in MarkdownHeading().Matches(text, previousStart + 1))
        {
            if (heading.Index >= end)
            {
                break;
            }

            nextStart = Math.Max(nextStart, heading.Index);
        }

        return nextStart;
    }

    [GeneratedRegex("^#{1,6}\\s+(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownHeading();
}
