using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>Performs tag-prefiltered hybrid lexical and exact-cosine Knowledge retrieval.</summary>
internal sealed class KnowledgeSearchService(SqliteKnowledgeStore store, IKnowledgeEmbeddingService embeddings, ITokenEstimator tokenEstimator, IOptionsMonitor<KnowledgeOptions> options) : IKnowledgeSearchService
{
    public async Task<KnowledgeSearchResponse> SearchAsync(string query, IReadOnlyList<string> owningTags, IReadOnlyList<string>? requestedTags, int? maximumResults, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var settings = options.CurrentValue;
        if (query.Length > settings.MaximumQueryCharacters) throw new ArgumentException("The Knowledge query exceeds the configured limit.", nameof(query));
        var tags = KnowledgeTagPolicy.Narrow(owningTags, requestedTags, KnowledgeTagPolicy.MaximumCallTags);
        if (tags.Count == 0) throw new KnowledgeAuthorizationException("Knowledge access requires at least one authorized tag.");
        var limit = Math.Min(maximumResults ?? settings.DefaultMaxResults, settings.MaximumResults);
        if (limit is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(maximumResults));
        await store.InitializeAsync(cancellationToken); await store.EnsureConfiguredContractAsync(settings, cancellationToken);
        var lexicalTask = store.SearchLexicalCandidatesAsync(tags, KnowledgeLexicalQuery.Create(query, settings), settings.LexicalCandidatePoolSize, cancellationToken);
        var vector = (await embeddings.GenerateAsync([query], cancellationToken))[0];
        var semantic = SelectSemantic(await store.GetCandidatesAsync(tags, cancellationToken), vector, settings.MinimumSimilarity, settings.SemanticCandidatePoolSize);
        var fused = Fuse(semantic, await lexicalTask, settings.RrfRankConstant);
        var anchors = Mmr(fused, limit, settings.MmrLambda);
        var output = new List<KnowledgeSearchResult>(); var usedTokens = 0;
        foreach (var anchor in anchors)
        {
            var span = await ExpandAsync(anchor.Value, output, usedTokens, settings, cancellationToken);
            var text = string.Join("\n\n---\n\n", span.OrderBy(x => x.Ordinal).Select(x => x.Text));
            var tokens = tokenEstimator.EstimateText(text); if (usedTokens + tokens > settings.DefaultMaximumResultTokens) continue;
            var citation = string.Join(" — ", new[] { anchor.Value.DocumentName, anchor.Value.Heading, anchor.Value.PageNumber is null ? null : $"page {anchor.Value.PageNumber}" }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var returnedText = span.Count == 1 ? Excerpt(text, settings.MaximumExcerptCharacters) : text;
            output.Add(new KnowledgeSearchResult(output.Count + 1, anchor.Similarity, anchor.Value.DocumentId, anchor.Value.DocumentName, anchor.Value.Description, anchor.Value.Tags, anchor.Value.ChunkId, anchor.Value.Ordinal, returnedText, citation, anchor.Value.Heading, anchor.Value.PageNumber, span.Min(x => x.Ordinal), span.Max(x => x.Ordinal), anchor.MatchType)); usedTokens += tokens;
        }
        var response = new KnowledgeSearchResponse("ok", tags, output);
        while (output.Count > 0 && ToolJson.Serialize(response).Length > settings.MaximumToolResultCharacters) { output.RemoveAt(output.Count - 1); response = new KnowledgeSearchResponse("ok", tags, output); }
        return response;
    }

    private async Task<IReadOnlyList<KnowledgeCandidate>> ExpandAsync(KnowledgeCandidate anchor, IReadOnlyList<KnowledgeSearchResult> output, int used, KnowledgeOptions settings, CancellationToken token)
    {
        var result = new List<KnowledgeCandidate> { anchor };
        var context = await store.GetContiguousContextAsync(anchor.ChunkId, settings.MaximumContextNeighborsEachSide, token);
        var previousAllowed = true;
        var nextAllowed = true;
        for (var distance = 1; distance <= settings.MaximumContextNeighborsEachSide; distance++)
        {
            if (previousAllowed)
            {
                var previous = context.FirstOrDefault(x => x.Ordinal == anchor.Ordinal - distance);
                previousAllowed = TryAddContextChunk(previous, anchor, result, output, used, settings);
            }

            if (nextAllowed)
            {
                var next = context.FirstOrDefault(x => x.Ordinal == anchor.Ordinal + distance);
                nextAllowed = TryAddContextChunk(next, anchor, result, output, used, settings);
            }
        }
        return result;
    }

    private bool TryAddContextChunk(KnowledgeCandidate? candidate, KnowledgeCandidate anchor, ICollection<KnowledgeCandidate> accepted, IReadOnlyList<KnowledgeSearchResult> output, int used, KnowledgeOptions settings)
    {
        if (candidate is null || !string.Equals(candidate.Heading ?? string.Empty, anchor.Heading ?? string.Empty, StringComparison.Ordinal) || (candidate.PageNumber is not null && anchor.PageNumber is not null && candidate.PageNumber != anchor.PageNumber) || output.Any(x => Overlaps(x.Text, candidate.Text))) return false;
        var proposedText = string.Join("\n\n---\n\n", accepted.Append(candidate).OrderBy(x => x.Ordinal).Select(x => x.Text));
        if (proposedText.Length > settings.MaximumExcerptCharacters) return false;
        if (used + tokenEstimator.EstimateText(proposedText) > settings.DefaultMaximumResultTokens) return false;
        accepted.Add(candidate);
        return true;
    }

    private static IReadOnlyList<Candidate> Fuse(IReadOnlyList<Candidate> semantic, IReadOnlyList<KnowledgeCandidate> lexical, int constant)
    {
        var values = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        for (var i = 0; i < semantic.Count; i++) values[semantic[i].Value.ChunkId] = semantic[i] with { RankScore = 1d / (constant + i + 1) };
        for (var i = 0; i < lexical.Count; i++) values[lexical[i].ChunkId] = values.TryGetValue(lexical[i].ChunkId, out var current) ? current with { RankScore = current.RankScore + 1d / (constant + i + 1), MatchType = "Matched by both" } : new Candidate(lexical[i], null, 1d / (constant + i + 1), "Matched by exact terms");
        return values.Values.OrderByDescending(x => x.RankScore).ThenByDescending(x => x.Similarity ?? float.MinValue).ThenBy(x => x.Value.ChunkId, StringComparer.Ordinal).ToArray();
    }
    private static IReadOnlyList<Candidate> SelectSemantic(IReadOnlyList<KnowledgeCandidate> candidates, float[] query, float minimumSimilarity, int capacity)
    {
        var ranked = new PriorityQueue<Candidate, SemanticPriority>(SemanticPriorityComparer.Instance);
        foreach (var value in candidates)
        {
            var similarity = Cosine(query, value.Vector);
            if (similarity < minimumSimilarity) continue;
            ranked.Enqueue(
                new Candidate(value, similarity, 0, "Matched semantically"),
                new SemanticPriority(similarity, value.ChunkId));
            if (ranked.Count > capacity) ranked.Dequeue();
        }
        return ranked.UnorderedItems.Select(item => item.Element).OrderByDescending(x => x.Similarity).ThenBy(x => x.Value.ChunkId, StringComparer.Ordinal).ToArray();
    }
    private static IReadOnlyList<Candidate> Mmr(IReadOnlyList<Candidate> pool, int limit, double lambda)
    {
        var pending = pool.ToList(); var selected = new List<Candidate>(); var min = pool.Count == 0 ? 0 : pool.Min(x => x.RankScore); var max = pool.Count == 0 ? 0 : pool.Max(x => x.RankScore);
        while (pending.Count > 0 && selected.Count < limit) { var next = selected.Count == 0 ? pending[0] : pending.OrderByDescending(x => lambda * (max == min ? 1 : (x.RankScore - min) / (max - min)) - (1 - lambda) * selected.Max(y => Cosine(x.Value.Vector, y.Value.Vector))).ThenBy(x => x.Value.ChunkId, StringComparer.Ordinal).First(); if (!selected.Any(x => Overlaps(x.Value.Text, next.Value.Text))) selected.Add(next); pending.Remove(next); }
        return selected;
    }
    private static float Cosine(float[] left, float[] right) { if (left.Length != right.Length || right.Any(x => !float.IsFinite(x))) throw new InvalidOperationException("The Knowledge index vector dimension does not match the query."); double total = 0; for (var i = 0; i < left.Length; i++) total += (double)left[i] * right[i]; return (float)total; }
    private static bool Overlaps(string left, string right) { var length = Math.Min(120, Math.Min(left.Length, right.Length)); return length > 24 && (left.Contains(right[..length], StringComparison.Ordinal) || right.Contains(left[^length..], StringComparison.Ordinal)); }
    private static string Excerpt(string text, int maximum) => text.Length <= maximum ? text : text[..maximum].TrimEnd() + "…";
    private sealed record Candidate(KnowledgeCandidate Value, float? Similarity, double RankScore, string MatchType);
    private readonly record struct SemanticPriority(float Similarity, string ChunkId);

    private sealed class SemanticPriorityComparer : IComparer<SemanticPriority>
    {
        public static SemanticPriorityComparer Instance { get; } = new();

        public int Compare(SemanticPriority left, SemanticPriority right)
        {
            var similarity = left.Similarity.CompareTo(right.Similarity);
            return similarity != 0
                ? similarity
                : StringComparer.Ordinal.Compare(right.ChunkId, left.ChunkId);
        }
    }
}
