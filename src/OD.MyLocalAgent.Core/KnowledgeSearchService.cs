using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>Performs tag-prefiltered portable exact-cosine retrieval.</summary>
internal sealed class KnowledgeSearchService : IKnowledgeSearchService
{
    private readonly SqliteKnowledgeStore _store;
    private readonly IKnowledgeEmbeddingService _embeddings;
    private readonly IOptionsMonitor<KnowledgeOptions> _options;

    /// <summary>Initializes the authorized search service.</summary>
    public KnowledgeSearchService(
        SqliteKnowledgeStore store,
        IKnowledgeEmbeddingService embeddings,
        IOptionsMonitor<KnowledgeOptions> options)
    {
        _store = store;
        _embeddings = embeddings;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<KnowledgeSearchResponse> SearchAsync(string query, IReadOnlyList<string> owningTags, IReadOnlyList<string>? requestedTags, int? maximumResults, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(owningTags);
        var options = _options.CurrentValue;
        if (query.Length > options.MaximumQueryCharacters)
        {
            throw new ArgumentException("The Knowledge query exceeds the configured limit.", nameof(query));
        }

        var effective = KnowledgeTagPolicy.Narrow(owningTags, requestedTags, KnowledgeTagPolicy.MaximumCallTags);
        if (effective.Count == 0)
        {
            throw new KnowledgeAuthorizationException("Knowledge access requires at least one authorized tag.");
        }

        var limit = maximumResults ?? options.DefaultMaxResults;
        if (limit is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        }

        limit = Math.Min(limit, options.MaximumResults);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _store.EnsureConfiguredContractAsync(options, cancellationToken).ConfigureAwait(false);
        var queryVector = (await _embeddings.GenerateAsync([query], cancellationToken).ConfigureAwait(false))[0];
        var candidates = await _store.GetCandidatesAsync(effective, cancellationToken).ConfigureAwait(false);
        var ranked = SelectTopCandidates(
            candidates,
            queryVector,
            options.MinimumSimilarity,
            checked(options.MaximumResults * 4));
        var selected = new List<KnowledgeSearchResult>();
        var tokens = 0;
        foreach (var item in ranked)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            var candidate = item.Candidate;
            if (tokens + candidate.EstimatedTokens > options.DefaultMaximumResultTokens)
            {
                continue;
            }

            if (selected.Any(existing => existing.DocumentId == candidate.DocumentId && Math.Abs(existing.ChunkOrdinal - candidate.Ordinal) == 1 && Overlaps(existing.Text, candidate.Text)))
            {
                continue;
            }

            var text = CreateExcerpt(candidate.Text, options.MaximumExcerptCharacters);
            var citation = string.Join(" — ", new[] { candidate.DocumentName, candidate.Heading, candidate.PageNumber is null ? null : $"page {candidate.PageNumber}" }.Where(value => !string.IsNullOrWhiteSpace(value)));
            selected.Add(new KnowledgeSearchResult(selected.Count + 1, item.Similarity, candidate.DocumentId, candidate.DocumentName, candidate.Description, candidate.Tags, candidate.ChunkId, candidate.Ordinal, text, citation, candidate.Heading, candidate.PageNumber));
            tokens += candidate.EstimatedTokens;
        }

        var response = new KnowledgeSearchResponse("ok", effective, selected);
        while (selected.Count > 0 && ToolJson.Serialize(response).Length > options.MaximumToolResultCharacters)
        {
            selected.RemoveAt(selected.Count - 1);
            response = new KnowledgeSearchResponse("ok", effective, selected);
        }
        return response;
    }

    private static IReadOnlyList<RankedCandidate> SelectTopCandidates(
        IReadOnlyList<KnowledgeCandidate> candidates,
        float[] queryVector,
        float minimumSimilarity,
        int capacity)
    {
        var queue = new PriorityQueue<RankedCandidate, RankPriority>(RankPriorityComparer.Instance);
        foreach (var candidate in candidates)
        {
            var similarity = Cosine(queryVector, candidate.Vector);
            if (similarity < minimumSimilarity)
            {
                continue;
            }

            var ranked = new RankedCandidate(candidate, similarity);
            queue.Enqueue(ranked, new RankPriority(similarity, candidate.ChunkId));
            if (queue.Count > capacity)
            {
                queue.Dequeue();
            }
        }

        var result = new List<RankedCandidate>(queue.Count);
        while (queue.TryDequeue(out var candidate, out _))
        {
            result.Add(candidate);
        }

        result.Sort(static (left, right) =>
        {
            var similarity = right.Similarity.CompareTo(left.Similarity);
            return similarity != 0
                ? similarity
                : StringComparer.Ordinal.Compare(left.Candidate.ChunkId, right.Candidate.ChunkId);
        });
        return result;
    }

    private static float Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length)
        {
            throw new InvalidOperationException("The Knowledge index vector dimension does not match the query.");
        }

        double sum = 0;
        for (var index = 0; index < left.Length; index++)
        {
            if (!float.IsFinite(right[index]))
            {
                throw new InvalidOperationException("The Knowledge index contains a non-finite vector.");
            }

            sum += (double)left[index] * right[index];
        }

        return (float)sum;
    }
    private static bool Overlaps(string left, string right) { var length = Math.Min(120, Math.Min(left.Length, right.Length)); return length > 24 && (left.Contains(right[..length], StringComparison.Ordinal) || right.Contains(left[^length..], StringComparison.Ordinal)); }
    private static string CreateExcerpt(string text, int maximumCharacters)
    {
        if (text.Length <= maximumCharacters) return text;
        var minimum = maximumCharacters / 2;
        for (var index = maximumCharacters - 1; index >= minimum; index--)
        {
            if (text[index] is '.' or '!' or '?') return text[..(index + 1)].TrimEnd();
        }
        for (var index = maximumCharacters - 1; index >= minimum; index--)
        {
            if (char.IsWhiteSpace(text[index])) return text[..index].TrimEnd() + "…";
        }
        return text[..maximumCharacters].TrimEnd() + "…";
    }

    private sealed record RankedCandidate(KnowledgeCandidate Candidate, float Similarity);

    private readonly record struct RankPriority(float Similarity, string ChunkId);

    private sealed class RankPriorityComparer : IComparer<RankPriority>
    {
        public static RankPriorityComparer Instance { get; } = new();

        public int Compare(RankPriority left, RankPriority right)
        {
            var similarity = left.Similarity.CompareTo(right.Similarity);
            return similarity != 0
                ? similarity
                : StringComparer.Ordinal.Compare(right.ChunkId, left.ChunkId);
        }
    }
}
