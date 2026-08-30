using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using PdfPageSize = UglyToad.PdfPig.Content.PageSize;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the Stage 26 authorization, persistence, and portable retrieval baseline.</summary>
public sealed class KnowledgeStage26Tests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "od-knowledge-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Verifies canonical normalization and fail-closed scope narrowing.</summary>
    [Fact]
    public void TagsNormalizeAndUnauthorizedNarrowingFails()
    {
        KnowledgeTagPolicy.NormalizeScope(["  .NET   Architecture ", "MAUI"]).Should().Equal(".net architecture", "maui");
        var action = () => KnowledgeTagPolicy.Narrow(["maui"], ["private"]);
        action.Should().Throw<KnowledgeAuthorizationException>();
        ((Action)(() => KnowledgeTagPolicy.NormalizeScope(["maui", "MAUI"]))).Should().Throw<ArgumentException>();
        ((Action)(() => KnowledgeTagPolicy.Normalize("*"))).Should().Throw<ArgumentException>();
    }

    /// <summary>Verifies caller text is converted to bounded literal FTS terms instead of FTS syntax.</summary>
    [Fact]
    public void LexicalQueryNeutralizesFtsOperatorsAndPunctuation()
    {
        var query = KnowledgeLexicalQuery.Create("foo* AND (bar) \"NEAR\"", new KnowledgeOptions());
        query.IsEmpty.Should().BeFalse();
        query.MatchExpression.Should().Be("\"foo\" OR \"AND\" OR \"bar\" OR \"NEAR\"");
        KnowledgeLexicalQuery.Create("*** () \0", new KnowledgeOptions()).IsEmpty.Should().BeTrue();
    }

    /// <summary>Verifies the committed hybrid-evaluation fixture remains complete and machine-readable.</summary>
    [Fact]
    public void HybridEvaluationFixtureIsWellFormed()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "knowledge-hybrid-evaluation.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var documents = document.RootElement.GetProperty("documents").EnumerateArray().ToArray();
        var cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        var chunkIds = documents.SelectMany(item => item.GetProperty("chunks").EnumerateArray()).Select(item => item.GetProperty("id").GetString()).ToHashSet(StringComparer.Ordinal);
        documents.Should().HaveCount(4);
        cases.Should().HaveCount(5);
        cases.Select(item => item.GetProperty("id").GetString()).Should().Contain(["fr-diacritics", "exact-identifier", "semantic-paraphrase", "diversity", "unauthorized"]);
        cases.Should().OnlyContain(item =>
            item.EnumerateObject().Any(property => property.NameEquals("query")) &&
            item.EnumerateObject().Any(property => property.NameEquals("tags")) &&
            item.EnumerateObject().Any(property => property.NameEquals("expectedChunkIds")));
        var expectedChunkIds = cases.SelectMany(item => item.GetProperty("expectedChunkIds").EnumerateArray()).Select(item => item.GetString()!).ToArray();
        expectedChunkIds.Should().OnlyContain(id => chunkIds.Contains(id));
    }

    /// <summary>Evaluates the committed synthetic corpus and enforces its hybrid quality gate.</summary>
    [Fact]
    public async Task HybridEvaluationFixtureMeetsQualityGate()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "knowledge-hybrid-evaluation.json");
        var fixture = JsonSerializer.Deserialize<KnowledgeEvaluationFixture>(
            await File.ReadAllTextAsync(fixturePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        foreach (var document in fixture.Documents)
        {
            var chunks = document.Chunks.Select(chunk => new KnowledgeChunk(chunk.Id, chunk.Ordinal, chunk.Text, 8, 0, chunk.Text.Length, chunk.Heading, chunk.Page)).ToArray();
            await store.CommitAsync(new KnowledgeDocument(document.Id, document.Id, string.Empty, document.Tags, $"{document.Id}.txt", "text/plain", 1, chunks.Sum(chunk => chunk.Text.Length), chunks.Length, now), $"{document.Id}.txt", chunks, document.Chunks.Select(chunk => chunk.Vector).ToArray(), "Ollama", "evaluation", CancellationToken.None);
        }

        var vectors = fixture.Cases.ToDictionary(item => item.Query, item => item.QueryVector, StringComparer.Ordinal);
        var settings = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "evaluation", MinimumSimilarity = 0.25f, MaximumContextNeighborsEachSide = 0 };
        var service = new KnowledgeSearchService(store, new MappingEmbeddings(vectors), Mock.Of<ITokenEstimator>(item => item.EstimateText(It.IsAny<string>()) == 8), Mock.Of<IOptionsMonitor<KnowledgeOptions>>(item => item.CurrentValue == settings));
        var privateChunkIds = fixture.Documents.Where(document => document.Tags.Contains("private", StringComparer.Ordinal)).SelectMany(document => document.Chunks).Select(chunk => chunk.Id).ToHashSet(StringComparer.Ordinal);
        var expectedCount = 0; var retrievedExpectedCount = 0; var reciprocalRanks = new List<double>(); var unauthorizedLeaks = 0;
        var duplicateResultCount = 0; var returnedTokenEstimate = 0; var returnedResultCount = 0;
        var semanticBaselineRetrievedCount = 0; var exactHybridRetrievedCount = 0; var exactBaselineRetrievedCount = 0;
        foreach (var item in fixture.Cases)
        {
            var response = await service.SearchAsync(item.Query, item.Tags, null, 5, CancellationToken.None);
            var resultIds = response.Results.Select(result => result.ChunkId).ToArray();
            unauthorizedLeaks += resultIds.Count(privateChunkIds.Contains);
            duplicateResultCount += resultIds.Length - resultIds.Distinct(StringComparer.Ordinal).Count();
            returnedTokenEstimate += response.Results.Count * 8;
            returnedResultCount += response.Results.Count;
            expectedCount += item.ExpectedChunkIds.Length;
            retrievedExpectedCount += item.ExpectedChunkIds.Count(resultIds.Contains);
            var semanticBaselineIds = fixture.Documents
                .Where(document => document.Tags.Intersect(item.Tags, StringComparer.Ordinal).Any())
                .SelectMany(document => document.Chunks)
                .Select(chunk => new { chunk.Id, Similarity = Dot(item.QueryVector, chunk.Vector) })
                .Where(candidate => candidate.Similarity >= settings.MinimumSimilarity)
                .OrderByDescending(candidate => candidate.Similarity)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .Take(5)
                .Select(candidate => candidate.Id)
                .ToArray();
            semanticBaselineRetrievedCount += item.ExpectedChunkIds.Count(semanticBaselineIds.Contains);
            if (item.Id is "fr-diacritics" or "exact-identifier")
            {
                exactHybridRetrievedCount += item.ExpectedChunkIds.Count(resultIds.Contains);
                exactBaselineRetrievedCount += item.ExpectedChunkIds.Count(semanticBaselineIds.Contains);
            }
            var firstExpectedRank = Array.FindIndex(resultIds, id => item.ExpectedChunkIds.Contains(id));
            if (firstExpectedRank >= 0) reciprocalRanks.Add(1d / (firstExpectedRank + 1));
        }

        var recallAtFive = (double)retrievedExpectedCount / expectedCount;
        var semanticBaselineRecallAtFive = (double)semanticBaselineRetrievedCount / expectedCount;
        var averageReturnedTokens = (double)returnedTokenEstimate / returnedResultCount;
        recallAtFive.Should().BeGreaterThanOrEqualTo(1d);
        recallAtFive.Should().BeGreaterThanOrEqualTo(semanticBaselineRecallAtFive);
        exactHybridRetrievedCount.Should().BeGreaterThanOrEqualTo(exactBaselineRetrievedCount);
        reciprocalRanks.Average().Should().BeGreaterThanOrEqualTo(0.8d);
        duplicateResultCount.Should().Be(0);
        averageReturnedTokens.Should().Be(8d);
        unauthorizedLeaks.Should().Be(0);
    }

    /// <summary>Verifies strict prompt-step policy round trips while legacy string form remains unchanged.</summary>
    [Fact]
    public void PromptToolPolicyRoundTripsAndLegacyFormIsStable()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacy = JsonSerializer.Deserialize<WorkflowStepDefinition>("\"ai.prompt\"", options)!;
        JsonSerializer.Serialize(legacy, options).Should().Be("\"ai.prompt\"");
        const string json = """{"step":"ai.prompt","args":{"prompt":"Search"},"toolAliases":["knowledge.search"],"knowledgeAccess":{"allowedTags":["maui"]}}""";
        var step = JsonSerializer.Deserialize<WorkflowStepDefinition>(json, options)!;
        step.ToolAliases.Should().Equal("knowledge.search");
        step.KnowledgeAccess!.AllowedTags.Should().Equal("maui");
        var roundTrip = JsonSerializer.Deserialize<WorkflowStepDefinition>(JsonSerializer.Serialize(step, options), options)!;
        roundTrip.Step.Should().Be(step.Step);
        roundTrip.Args.GetRawText().Should().Be(step.Args.GetRawText());
        roundTrip.ToolAliases.Should().Equal(step.ToolAliases);
        roundTrip.KnowledgeAccess.Should().BeEquivalentTo(step.KnowledgeAccess);
    }

    /// <summary>Verifies tag filtering happens before exact cosine ranking and empty scopes avoid embeddings.</summary>
    [Fact]
    public async Task PortableSearchIsPrefilteredAndAuthorizationPrecedesEmbedding()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-20T10:00:00Z");
        await store.CommitAsync(new KnowledgeDocument("a", "Allowed", "", ["public"], "a.txt", "text/plain", 1, 5, 1, now), "a.txt", [new KnowledgeChunk("a:0", 0, "allowed text", 3, 0, 12, null, null)], [[1f, 0f]], "Ollama", "test", CancellationToken.None);
        await store.CommitAsync(new KnowledgeDocument("b", "Denied", "", ["private"], "b.txt", "text/plain", 1, 5, 1, now), "b.txt", [new KnowledgeChunk("b:0", 0, "denied text", 3, 0, 11, null, null)], [[1f, 0f]], "Ollama", "test", CancellationToken.None);
        var embeddings = new RecordingEmbeddings([1f, 0f]); var options = Options(["public"]);
        var service = new KnowledgeSearchService(store, embeddings, Mock.Of<ITokenEstimator>(item => item.EstimateText(It.IsAny<string>()) == 3), options);
        var response = await service.SearchAsync("query", ["public"], null, 5, CancellationToken.None);
        response.Results.Should().ContainSingle().Which.DocumentId.Should().Be("a");
        var denied = async () => await service.SearchAsync("query", [], null, 5, CancellationToken.None);
        await denied.Should().ThrowAsync<KnowledgeAuthorizationException>();
        embeddings.CallCount.Should().Be(1);
    }

    /// <summary>Verifies FTS5 is built from active chunks and applies tag scope before its result limit.</summary>
    [Fact]
    public async Task HybridLexicalSearchUsesActiveAuthorizedChunks()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        await store.CommitAsync(new KnowledgeDocument("allowed", "Guide", "", ["public"], "a.txt", "text/plain", 1, 1, 1, now), "a.txt", [new KnowledgeChunk("allowed:0", 0, "Résumé IServiceProvider startup error", 5, 0, 36, "Setup", null)], [[1f, 0f]], "Ollama", "test", CancellationToken.None);
        await store.CommitAsync(new KnowledgeDocument("denied", "Secret", "", ["private"], "b.txt", "text/plain", 1, 1, 1, now), "b.txt", [new KnowledgeChunk("denied:0", 0, "IServiceProvider startup error secret", 5, 0, 37, "Setup", null)], [[1f, 0f]], "Ollama", "test", CancellationToken.None);

        var query = KnowledgeLexicalQuery.Create("resume IServiceProvider AND \"startup\"", new KnowledgeOptions());
        var results = await store.SearchLexicalCandidatesAsync(["public"], query, 20, CancellationToken.None);

        results.Should().ContainSingle().Which.ChunkId.Should().Be("allowed:0");
    }

    /// <summary>Verifies hybrid fusion retains a lexical hit and MMR prefers a complementary second anchor.</summary>
    [Fact]
    public async Task HybridSearchFusesExactTermsAndDiversifiesAnchors()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        await store.CommitAsync(new KnowledgeDocument("a", "Startup", "", ["public"], "a.txt", "text/plain", 1, 1, 1, now), "a.txt", [new KnowledgeChunk("a:0", 0, "IServiceProvider error code E-42", 5, 0, 31, null, null)], [[1f, 0f]], "Ollama", "test", CancellationToken.None);
        await store.CommitAsync(new KnowledgeDocument("b", "Duplicate", "", ["public"], "b.txt", "text/plain", 1, 1, 1, now), "b.txt", [new KnowledgeChunk("b:0", 0, "A very similar startup diagnostic", 5, 0, 31, null, null)], [[0.9949874f, 0.1f]], "Ollama", "test", CancellationToken.None);
        await store.CommitAsync(new KnowledgeDocument("c", "Recovery", "", ["public"], "c.txt", "text/plain", 1, 1, 1, now), "c.txt", [new KnowledgeChunk("c:0", 0, "Recover the service by restarting the host", 6, 0, 42, null, null)], [[0f, 1f]], "Ollama", "test", CancellationToken.None);
        var retrieval = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "test", MinimumSimilarity = 0, DefaultMaximumResultTokens = 100, MaximumResultTokens = 100 };
        var monitor = Mock.Of<IOptionsMonitor<KnowledgeOptions>>(item => item.CurrentValue == retrieval);
        var service = new KnowledgeSearchService(store, new RecordingEmbeddings([1f, 0f]), Mock.Of<ITokenEstimator>(item => item.EstimateText(It.IsAny<string>()) == 5), monitor);

        var response = await service.SearchAsync("IServiceProvider", ["public"], null, 2, CancellationToken.None);

        response.Results.Select(x => x.ChunkId).Should().Equal("a:0", "c:0");
        response.Results[0].MatchType.Should().Be("Matched by both");
    }

    /// <summary>Verifies small-to-big expansion includes only contiguous same-section, same-page neighbors.</summary>
    [Fact]
    public async Task HybridSearchExpandsOnlyPermittedAdjacentContext()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        var document = new KnowledgeDocument("guide", "Guide", "", ["public"], "guide.txt", "text/plain", 1, 1, 4, now);
        var chunks = new[]
        {
            new KnowledgeChunk("guide:0", 0, "Step before", 2, 0, 11, "Setup", 1),
            new KnowledgeChunk("guide:1", 1, "marker anchor", 2, 12, 25, "Setup", 1),
            new KnowledgeChunk("guide:2", 2, "Step after", 2, 26, 36, "Setup", 1),
            new KnowledgeChunk("guide:3", 3, "Different section", 2, 37, 54, "Other", 1),
        };
        await store.CommitAsync(document, "guide.txt", chunks, [[0f, 1f], [1f, 0f], [0f, 1f], [0f, 1f]], "Ollama", "test", CancellationToken.None);
        var retrieval = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "test", MaximumContextNeighborsEachSide = 2, DefaultMaximumResultTokens = 100, MaximumResultTokens = 100 };
        var service = new KnowledgeSearchService(store, new RecordingEmbeddings([1f, 0f]), Mock.Of<ITokenEstimator>(item => item.EstimateText(It.IsAny<string>()) == 5), Mock.Of<IOptionsMonitor<KnowledgeOptions>>(item => item.CurrentValue == retrieval));

        var response = await service.SearchAsync("marker", ["public"], null, 1, CancellationToken.None);

        var result = response.Results.Should().ContainSingle().Which;
        result.ContextStartChunkOrdinal.Should().Be(0);
        result.ContextEndChunkOrdinal.Should().Be(2);
        result.Text.Should().Contain("Step before").And.Contain("Step after").And.NotContain("Different section");
    }

    /// <summary>Verifies context expansion stops at a page boundary even when the heading is unchanged.</summary>
    [Fact]
    public async Task HybridSearchDoesNotExpandAcrossPageBoundary()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        var chunks = new[]
        {
            new KnowledgeChunk("page:0", 0, "marker on page one", 3, 0, 18, "Procedure", 1),
            new KnowledgeChunk("page:1", 1, "must remain on page two", 4, 19, 43, "Procedure", 2),
        };
        await store.CommitAsync(new KnowledgeDocument("page", "Pages", "", ["public"], "page.txt", "text/plain", 1, 1, 2, now), "page.txt", chunks, [[1f, 0f], [0f, 1f]], "Ollama", "test", CancellationToken.None);
        var settings = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "test", MaximumContextNeighborsEachSide = 1, DefaultMaximumResultTokens = 100, MaximumResultTokens = 100 };
        var service = new KnowledgeSearchService(store, new RecordingEmbeddings([1f, 0f]), Mock.Of<ITokenEstimator>(item => item.EstimateText(It.IsAny<string>()) == 5), Mock.Of<IOptionsMonitor<KnowledgeOptions>>(item => item.CurrentValue == settings));

        var result = (await service.SearchAsync("marker", ["public"], null, 1, CancellationToken.None)).Results.Should().ContainSingle().Which;

        result.ContextStartChunkOrdinal.Should().Be(0);
        result.ContextEndChunkOrdinal.Should().Be(0);
        result.Text.Should().NotContain("page two");
    }

    /// <summary>Verifies an oversized neighbor is omitted while its complete anchor remains available.</summary>
    [Fact]
    public async Task HybridSearchPreservesAnchorWhenNeighborExceedsDisplayBudget()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        var chunks = new[]
        {
            new KnowledgeChunk("budget:0", 0, "marker anchor", 2, 0, 13, "Procedure", 1),
            new KnowledgeChunk("budget:1", 1, "this neighbor would exceed the display budget", 8, 14, 58, "Procedure", 1),
        };
        await store.CommitAsync(new KnowledgeDocument("budget", "Budget", "", ["public"], "budget.txt", "text/plain", 1, 1, 2, now), "budget.txt", chunks, [[1f, 0f], [0f, 1f]], "Ollama", "test", CancellationToken.None);
        var settings = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "test", MaximumContextNeighborsEachSide = 1, MaximumExcerptCharacters = 20, DefaultMaximumResultTokens = 100, MaximumResultTokens = 100 };
        var service = new KnowledgeSearchService(store, new RecordingEmbeddings([1f, 0f]), Mock.Of<ITokenEstimator>(item => item.EstimateText(It.IsAny<string>()) == 5), Mock.Of<IOptionsMonitor<KnowledgeOptions>>(item => item.CurrentValue == settings));

        var result = (await service.SearchAsync("marker", ["public"], null, 1, CancellationToken.None)).Results.Should().ContainSingle().Which;

        result.Text.Should().Be("marker anchor");
        result.ContextStartChunkOrdinal.Should().Be(0);
        result.ContextEndChunkOrdinal.Should().Be(0);
    }

    /// <summary>Verifies response serialization stays below the configured tool-result character cap.</summary>
    [Fact]
    public async Task HybridSearchHonorsFinalSerializedCharacterCap()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        var text = "marker " + new string('x', 10_000);
        await store.CommitAsync(new KnowledgeDocument("cap", "Character cap", "", ["public"], "cap.txt", "text/plain", 1, text.Length, 1, now), "cap.txt", [new KnowledgeChunk("cap:0", 0, text, 20, 0, text.Length, null, null)], [[1f, 0f]], "Ollama", "test", CancellationToken.None);
        var settings = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "test", MaximumExcerptCharacters = 3_000, MaximumToolResultCharacters = 4_096 };
        var service = new KnowledgeSearchService(store, new RecordingEmbeddings([1f, 0f]), Mock.Of<ITokenEstimator>(item => item.EstimateText(It.IsAny<string>()) == 20), Mock.Of<IOptionsMonitor<KnowledgeOptions>>(item => item.CurrentValue == settings));

        var response = await service.SearchAsync("marker", ["public"], null, 1, CancellationToken.None);

        response.Results.Should().ContainSingle();
        ToolJson.Serialize(response).Length.Should().BeLessThanOrEqualTo(settings.MaximumToolResultCharacters);
    }

    /// <summary>Verifies startup repairs a missing FTS table from active chunk rows without re-embedding.</summary>
    [Fact]
    public async Task StartupRebuildsMissingFtsTable()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        await store.CommitAsync(new KnowledgeDocument("a", "Guide", "", ["public"], "a.txt", "text/plain", 1, 1, 1, now), "a.txt", [new KnowledgeChunk("a:0", 0, "recovery marker", 2, 0, 15, null, null)], [[1f, 0f]], "Ollama", "test", CancellationToken.None);
        await using (var connection = new SqliteConnection($"Data Source={paths.KnowledgeDatabasePath};Pooling=False"))
        {
            await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE chunk_fts;"; await command.ExecuteNonQueryAsync();
        }

        await store.InitializeAsync(CancellationToken.None);
        var results = await store.SearchLexicalCandidatesAsync(["public"], KnowledgeLexicalQuery.Create("marker", new KnowledgeOptions()), 20, CancellationToken.None);
        results.Should().ContainSingle().Which.ChunkId.Should().Be("a:0");
    }

    /// <summary>Verifies a schema-3 library upgrades active chunks to FTS without re-embedding.</summary>
    [Fact]
    public async Task VersionThreeKnowledgeSchemaMigratesActiveChunksToFts()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        await store.CommitAsync(new KnowledgeDocument("legacy3", "Legacy", "", ["public"], "legacy.txt", "text/plain", 1, 1, 1, now), "legacy.txt", [new KnowledgeChunk("legacy3:0", 0, "schema three marker", 3, 0, 19, null, null)], [[1f, 0f]], "Ollama", "test", CancellationToken.None);
        await using (var connection = new SqliteConnection($"Data Source={paths.KnowledgeDatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE chunk_fts; PRAGMA user_version=3;";
            await command.ExecuteNonQueryAsync();
        }

        await store.InitializeAsync(CancellationToken.None);

        (await store.SearchLexicalCandidatesAsync(["public"], KnowledgeLexicalQuery.Create("marker", new KnowledgeOptions()), 20, CancellationToken.None)).Should().ContainSingle().Which.ChunkId.Should().Be("legacy3:0");
        await using var check = new SqliteConnection($"Data Source={paths.KnowledgeDatabasePath};Pooling=False");
        await check.OpenAsync();
        await using var version = check.CreateCommand(); version.CommandText = "PRAGMA user_version;";
        (await version.ExecuteScalarAsync()).Should().Be(4L);
    }

    /// <summary>Verifies startup replaces a structurally corrupt FTS table from active chunks.</summary>
    [Fact]
    public async Task StartupRebuildsCorruptFtsTable()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        await store.CommitAsync(new KnowledgeDocument("corrupt", "Corrupt", "", ["public"], "corrupt.txt", "text/plain", 1, 1, 1, now), "corrupt.txt", [new KnowledgeChunk("corrupt:0", 0, "corrupt marker", 2, 0, 14, null, null)], [[1f, 0f]], "Ollama", "test", CancellationToken.None);
        await using (var connection = new SqliteConnection($"Data Source={paths.KnowledgeDatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE chunk_fts; CREATE TABLE chunk_fts(invalid_column TEXT);";
            await command.ExecuteNonQueryAsync();
        }

        await store.InitializeAsync(CancellationToken.None);

        (await store.SearchLexicalCandidatesAsync(["public"], KnowledgeLexicalQuery.Create("marker", new KnowledgeOptions()), 20, CancellationToken.None)).Should().ContainSingle().Which.ChunkId.Should().Be("corrupt:0");
    }

    /// <summary>Verifies a failed schema-3 FTS migration leaves the previous schema version and chunks intact.</summary>
    [Fact]
    public async Task FailedFtsMigrationDoesNotAdvanceSchemaOrDiscardChunks()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        await store.CommitAsync(new KnowledgeDocument("migration", "Migration", "", ["public"], "migration.txt", "text/plain", 1, 1, 1, now), "migration.txt", [new KnowledgeChunk("migration:0", 0, "migration marker", 2, 0, 16, null, null)], [[1f, 0f]], "Ollama", "test", CancellationToken.None);
        await using (var connection = new SqliteConnection($"Data Source={paths.KnowledgeDatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE chunk_fts; CREATE TABLE chunk_fts(invalid_column TEXT); PRAGMA user_version=3;";
            await command.ExecuteNonQueryAsync();
        }

        var action = async () => await store.InitializeAsync(CancellationToken.None);
        await action.Should().ThrowAsync<SqliteException>();

        await using var check = new SqliteConnection($"Data Source={paths.KnowledgeDatabasePath};Pooling=False");
        await check.OpenAsync();
        await using var version = check.CreateCommand(); version.CommandText = "PRAGMA user_version;";
        (await version.ExecuteScalarAsync()).Should().Be(3L);
        await using var chunks = check.CreateCommand(); chunks.CommandText = "SELECT COUNT(*) FROM chunks;";
        (await chunks.ExecuteScalarAsync()).Should().Be(1L);
    }

    /// <summary>Verifies deterministic bounded chunk generation.</summary>
    [Fact]
    public void ChunkingIsDeterministicAndBounded()
    {
        var estimator = new Mock<ITokenEstimator>(); estimator.Setup(item => item.EstimateText(It.IsAny<string>())).Returns<string>(text => Math.Max(1, text.Length / 4));
        var options = Mock.Of<IOptionsMonitor<KnowledgeOptions>>(item => item.CurrentValue == new KnowledgeOptions { TargetChunkTokens = 32, MaximumChunkTokens = 32, OverlapChunkTokens = 4 });
        var chunker = new KnowledgeChunker(estimator.Object, options); var text = string.Join("\n\n", Enumerable.Repeat("A deterministic paragraph with enough words to create chunks.", 12));
        chunker.Chunk(text).Should().BeEquivalentTo(chunker.Chunk(text), configuration => configuration.WithStrictOrdering());
        chunker.Chunk(text).Should().OnlyContain(chunk => chunk.Text.Length <= 128);
    }

    /// <summary>Verifies PDF extraction keeps one-based page metadata through chunking.</summary>
    [Fact]
    public async Task PdfChunksRetainPageNumbers()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "pages.pdf");
        using (var builder = new PdfDocumentBuilder())
        {
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            builder.AddPage(PdfPageSize.A4).AddText("First page knowledge", 12, new PdfPoint(50, 750), font);
            builder.AddPage(PdfPageSize.A4).AddText("Second page knowledge", 12, new PdfPoint(50, 750), font);
            await File.WriteAllBytesAsync(path, builder.Build());
        }

        var options = Mock.Of<IOptionsMonitor<KnowledgeOptions>>(monitor => monitor.CurrentValue == new KnowledgeOptions());
        var extractor = new KnowledgeDocumentExtractor(options);
        var extracted = await extractor.ExtractAsync(path, CancellationToken.None);
        extracted.Blocks.Should().HaveCount(2);
        extracted.Blocks.Select(block => block.PageNumber).Should().Equal(1, 2);

        var estimator = new Mock<ITokenEstimator>();
        estimator.Setup(item => item.EstimateText(It.IsAny<string>())).Returns(10);
        var chunks = new KnowledgeChunker(estimator.Object, options).Chunk(extracted);
        chunks.Should().Contain(chunk => chunk.PageNumber == 1 && chunk.Text.Contains("First page", StringComparison.Ordinal));
    }

    /// <summary>Verifies HTML and HTM sources are converted internally to safe structured Markdown.</summary>
    [Theory]
    [InlineData(".html")]
    [InlineData(".htm")]
    public async Task HtmlExtractionProducesSafeStructuredMarkdown(string extension)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"guide{extension}");
        await File.WriteAllTextAsync(
            path,
            "<html><body><main><h1>Setup guide</h1><p>Install <strong>locally</strong>.</p><ul><li>First step</li></ul><script>unsafe()</script></main></body></html>");
        var options = Mock.Of<IOptionsMonitor<KnowledgeOptions>>(
            monitor => monitor.CurrentValue == new KnowledgeOptions());

        var extracted = await new KnowledgeDocumentExtractor(options)
            .ExtractAsync(path, CancellationToken.None);

        extracted.Text.Should().Contain("# Setup guide");
        extracted.Text.Should().Contain("Install **locally**.");
        extracted.Text.Should().Contain("- First step");
        extracted.Text.Should().NotContain("unsafe()");
    }

    /// <summary>Verifies HTML import is native and reports each visible ingestion phase.</summary>
    [Fact]
    public async Task HtmlImportReportsIngestionProgress()
    {
        var paths = new WorkspacePaths(_directory);
        paths.EnsureDirectories();
        var source = Path.Combine(_directory, "knowledge.html");
        await File.WriteAllTextAsync(
            source,
            "<html><body><article><h1>Knowledge</h1><p>Searchable content.</p></article></body></html>");
        var options = Mock.Of<IOptionsMonitor<KnowledgeOptions>>(
            monitor => monitor.CurrentValue == new KnowledgeOptions());
        var extractor = new KnowledgeDocumentExtractor(options);
        var library = CreateLibrary(paths, options, extractor, new RecordingEmbeddings([1f, 0f]));
        var phases = new List<string>();

        var document = await library.ImportAsync(
            source,
            "HTML knowledge",
            string.Empty,
            ["public"],
            CancellationToken.None,
            progress: new RecordingProgress(value => phases.Add(value.Phase)));

        document.MediaType.Should().Be("text/html");
        document.OriginalFileName.Should().Be("knowledge.html");
        phases.Should().Equal(
            "Preparing",
            "Copying",
            "Checking content",
            "Extracting",
            "Chunking",
            "Embedding",
            "Saving",
            "Complete");
        (await library.GetChunkPreviewAsync(document.Id, 20, CancellationToken.None))
            .Should().ContainSingle().Which.Text.Should().Contain("# Knowledge");
    }

    /// <summary>Verifies provider-neutral generation batches requests and normalizes vectors.</summary>
    [Fact]
    public async Task EmbeddingServiceBatchesAndNormalizesVectors()
    {
        var options = new KnowledgeOptions { EmbeddingBatchSize = 2 };
        var monitor = Mock.Of<IOptionsMonitor<KnowledgeOptions>>(item => item.CurrentValue == options);
        var generator = new RecordingEmbeddingGenerator();
        var factory = Mock.Of<IKnowledgeEmbeddingGeneratorFactory>(item => item.Create(options) == generator);
        var service = new KnowledgeEmbeddingService(monitor, factory);

        var vectors = await service.GenerateAsync(["one", "two", "three"], CancellationToken.None);

        generator.BatchSizes.Should().Equal(2, 1);
        vectors.Should().HaveCount(3);
        vectors.Should().OnlyContain(vector => Math.Abs(Math.Sqrt(vector.Sum(value => value * value)) - 1) < 0.0001);
    }

    /// <summary>Verifies cleanup intent survives until the canonical file deletion succeeds.</summary>
    [Fact]
    public async Task PendingCanonicalCleanupIsPersistedAndCompleted()
    {
        var paths = new WorkspacePaths(_directory);
        paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths);
        await store.InitializeAsync(CancellationToken.None);

        await store.QueueFileCleanupAsync("retired.txt", CancellationToken.None);
        (await store.GetPendingFileCleanupAsync(CancellationToken.None)).Should().Equal("retired.txt");
        await store.CompleteFileCleanupAsync("retired.txt", CancellationToken.None);
        (await store.GetPendingFileCleanupAsync(CancellationToken.None)).Should().BeEmpty();

        await using var connection = new SqliteConnection($"Data Source={paths.KnowledgeDatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        (await command.ExecuteScalarAsync()).Should().Be(4L);
    }

    /// <summary>Verifies startup removes abandoned staging and unreferenced canonical files only.</summary>
    [Fact]
    public async Task StartupReconcilesAbandonedKnowledgeFiles()
    {
        var paths = new WorkspacePaths(_directory);
        paths.EnsureDirectories();
        var staging = Path.Combine(paths.KnowledgeStagingDirectory, "abandoned.tmp");
        var orphan = Path.Combine(paths.KnowledgeDocumentsDirectory, "orphan.txt");
        await File.WriteAllTextAsync(staging, "staged");
        await File.WriteAllTextAsync(orphan, "orphan");
        var options = Mock.Of<IOptionsMonitor<KnowledgeOptions>>(item => item.CurrentValue == new KnowledgeOptions());
        var library = CreateLibrary(
            paths,
            options,
            Mock.Of<IKnowledgeDocumentExtractor>(),
            Mock.Of<IKnowledgeEmbeddingService>());

        await library.InitializeAsync(CancellationToken.None);

        File.Exists(staging).Should().BeFalse();
        File.Exists(orphan).Should().BeFalse();
    }

    /// <summary>Verifies independent readers remain usable while one metadata writer commits.</summary>
    [Fact]
    public async Task StoreSupportsConcurrentReadersAndWriter()
    {
        var paths = new WorkspacePaths(_directory);
        paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths);
        await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-20T10:00:00Z");
        await store.CommitAsync(
            new KnowledgeDocument("a", "Guide", "", ["public"], "a.txt", "text/plain", 8, 8, 1, now),
            "a.txt",
            [new KnowledgeChunk("a:0", 0, "old text", 2, 0, 8, null, null)],
            [[1f, 0f]],
            "Ollama",
            "test",
            CancellationToken.None);

        var readers = Enumerable.Range(0, 2).Select(async _ =>
        {
            for (var index = 0; index < 10; index++)
            {
                (await store.GetCandidatesAsync(["public"], CancellationToken.None)).Should().ContainSingle();
            }
        });
        var writer = store.UpdateDetailsAsync("a", "Updated", "", ["public"], now.AddMinutes(1), CancellationToken.None);

        await Task.WhenAll(readers.Append(writer));
        (await store.ListAsync(CancellationToken.None)).Should().ContainSingle().Which.Name.Should().Be("Updated");
    }

    /// <summary>Verifies canceled pre-commit ingestion leaves no searchable or staged revision.</summary>
    [Fact]
    public async Task CanceledImportLeavesNoPartialRevision()
    {
        var paths = new WorkspacePaths(_directory);
        paths.EnsureDirectories();
        var source = Path.Combine(_directory, "source.txt");
        await File.WriteAllTextAsync(source, "bounded knowledge text");
        var options = Mock.Of<IOptionsMonitor<KnowledgeOptions>>(item => item.CurrentValue == new KnowledgeOptions { MinimumSimilarity = 0 });
        var extractor = Mock.Of<IKnowledgeDocumentExtractor>(item => item.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult(new KnowledgeExtractedDocument("bounded knowledge text", new KnowledgeExtractedBlock[] { new(0, 22, null, null) })));
        var embeddings = new Mock<IKnowledgeEmbeddingService>();
        embeddings.Setup(item => item.GenerateAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var library = CreateLibrary(paths, options, extractor, embeddings.Object);

        var action = async () => await library.ImportAsync(source, "Canceled", "", ["public"], CancellationToken.None);

        await action.Should().ThrowAsync<OperationCanceledException>();
        (await library.GetDocumentsAsync(CancellationToken.None)).Should().BeEmpty();
        Directory.EnumerateFiles(paths.KnowledgeStagingDirectory).Should().BeEmpty();
        Directory.EnumerateFiles(paths.KnowledgeDocumentsDirectory).Should().BeEmpty();
    }

    /// <summary>Verifies the private copy survives source deletion and duplicate import requires an explicit override.</summary>
    [Fact]
    public async Task ImportOwnsCanonicalCopyAndDuplicatePolicyIsExplicit()
    {
        var paths = new WorkspacePaths(_directory);
        paths.EnsureDirectories();
        var source = Path.Combine(_directory, "source.txt");
        await File.WriteAllTextAsync(source, "bounded knowledge text");
        var options = Mock.Of<IOptionsMonitor<KnowledgeOptions>>(item => item.CurrentValue == new KnowledgeOptions { MinimumSimilarity = 0 });
        var extractor = Mock.Of<IKnowledgeDocumentExtractor>(item => item.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult(new KnowledgeExtractedDocument("bounded knowledge text", new KnowledgeExtractedBlock[] { new(0, 22, null, null) })));
        var embeddings = new RecordingEmbeddings([1f, 0f]);
        var library = CreateLibrary(paths, options, extractor, embeddings);

        var first = await library.ImportAsync(source, "First", "", ["public"], CancellationToken.None);
        var duplicate = async () => await library.ImportAsync(source, "Second", "", ["public"], CancellationToken.None);
        await duplicate.Should().ThrowAsync<KnowledgeDuplicateException>();
        var second = await library.ImportAsync(source, "Second", "", ["public"], CancellationToken.None, allowDuplicate: true);
        File.Delete(source);

        File.Exists(Path.Combine(paths.KnowledgeDocumentsDirectory, first.Id + ".txt")).Should().BeTrue();
        File.Exists(Path.Combine(paths.KnowledgeDocumentsDirectory, second.Id + ".txt")).Should().BeTrue();
        (await library.GetDocumentsAsync(CancellationToken.None)).Should().HaveCount(2);
    }

    /// <summary>Verifies a newer database is rejected without attempting a downgrade.</summary>
    [Fact]
    public async Task NewerKnowledgeSchemaIsRejected()
    {
        var paths = new WorkspacePaths(_directory);
        paths.EnsureDirectories();
        SqliteNativeBootstrap.Initialize();
        await using (var connection = new SqliteConnection($"Data Source={paths.KnowledgeDatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=999;";
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteKnowledgeStore(paths);
        var action = async () => await store.InitializeAsync(CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*newer than supported*");
    }

    /// <summary>Verifies schema 2 adds the durable cleanup queue idempotently.</summary>
    [Fact]
    public async Task VersionTwoKnowledgeSchemaMigratesCleanupQueue()
    {
        var paths = new WorkspacePaths(_directory);
        paths.EnsureDirectories();
        SqliteNativeBootstrap.Initialize();
        await using (var connection = new SqliteConnection($"Data Source={paths.KnowledgeDatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=2;";
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteKnowledgeStore(paths);
        await store.InitializeAsync(CancellationToken.None);
        await store.InitializeAsync(CancellationToken.None);
        await store.QueueFileCleanupAsync("retired.txt", CancellationToken.None);

        (await store.GetPendingFileCleanupAsync(CancellationToken.None)).Should().Equal("retired.txt");
    }

    /// <summary>Verifies incompatible embedding settings are detected before a mixed-space write or search.</summary>
    [Fact]
    public async Task IndexContractMismatchRequiresExplicitReindex()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-20T10:00:00Z");
        var indexed = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "embeddinggemma:300m" };
        await store.CommitAsync(
            new KnowledgeDocument("a", "Allowed", "", ["public"], "a.txt", "text/plain", 1, 5, 1, now),
            "a.txt", [new KnowledgeChunk("a:0", 0, "allowed text", 3, 0, 12, null, null)], [[1f, 0f]], indexed, CancellationToken.None);

        var changed = indexed with { EmbeddingModel = "all-minilm:latest" };
        var status = await store.GetIndexStatusAsync(changed, CancellationToken.None);
        status.HasIndex.Should().BeTrue();
        status.RequiresReindex.Should().BeTrue();
        var action = async () => await store.EnsureConfiguredContractAsync(changed, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Reindex the library*");
    }

    /// <summary>Verifies a prepared revision becomes searchable atomically and retires the previous revision.</summary>
    [Fact]
    public async Task ReplacementActivatesOneCompleteRevision()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-20T10:00:00Z");
        var options = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "embeddinggemma:300m" };
        var original = new KnowledgeDocument("a", "Guide", "", ["public"], "a.txt", "text/plain", 10, 8, 1, now);
        await store.CommitAsync(original, "old.txt", [new KnowledgeChunk("old:0", 0, "old text", 2, 0, 8, null, null)], [[1f, 0f]], options, CancellationToken.None);
        var replacement = original with { OriginalFileName = "new.txt", CharacterCount = 8, UpdatedUtc = now.AddMinutes(1) };
        var prepared = new KnowledgePreparedIndex(
            replacement, "new.txt", [new KnowledgeChunk("new-revision:0", 0, "new text", 2, 0, 8, null, null)], [[0f, 1f]],
            "new-revision", new string('a', 64), new string('b', 64));

        var retiredFile = await store.ReplaceAsync(prepared, options, CancellationToken.None);
        retiredFile.Should().Be("old.txt");
        var candidates = await store.GetCandidatesAsync(["public"], CancellationToken.None);
        candidates.Should().ContainSingle().Which.Text.Should().Be("new text");
        candidates[0].ChunkId.Should().Be("new-revision:0");
        (await store.SearchLexicalCandidatesAsync(["public"], KnowledgeLexicalQuery.Create("new", options), 10, CancellationToken.None)).Should().ContainSingle().Which.ChunkId.Should().Be("new-revision:0");
        (await store.SearchLexicalCandidatesAsync(["public"], KnowledgeLexicalQuery.Create("old", options), 10, CancellationToken.None)).Should().BeEmpty();
        (await store.FindActiveBySourceHashAsync(new string('a', 64), CancellationToken.None))!.Id.Should().Be("a");
        (await store.GetChunkPreviewAsync("a", 10, 1000, CancellationToken.None)).Should().ContainSingle().Which.Text.Should().Be("new text");
    }

    /// <summary>Verifies deleting a document removes its active FTS rows in the same transaction.</summary>
    [Fact]
    public async Task DeletionRemovesLexicalCandidates()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        var options = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "test" };
        await store.CommitAsync(new KnowledgeDocument("delete", "Delete", "", ["public"], "delete.txt", "text/plain", 1, 1, 1, now), "delete.txt", [new KnowledgeChunk("delete:0", 0, "delete lexical marker", 3, 0, 21, null, null)], [[1f, 0f]], options, CancellationToken.None);

        (await store.DeleteAsync("delete", CancellationToken.None)).Deleted.Should().BeTrue();

        (await store.SearchLexicalCandidatesAsync(["public"], KnowledgeLexicalQuery.Create("marker", options), 10, CancellationToken.None)).Should().BeEmpty();
    }

    /// <summary>Verifies a full-library reindex replaces the lexical corpus atomically.</summary>
    [Fact]
    public async Task FullLibraryReindexReplacesLexicalCorpus()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        var options = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "test" };
        var alpha = new KnowledgeDocument("alpha", "Alpha", "", ["public"], "alpha.txt", "text/plain", 1, 1, 1, now);
        var beta = new KnowledgeDocument("beta", "Beta", "", ["public"], "beta.txt", "text/plain", 1, 1, 1, now);
        await store.CommitAsync(alpha, "alpha.txt", [new KnowledgeChunk("alpha:old", 0, "old alpha", 2, 0, 9, null, null)], [[1f, 0f]], options, CancellationToken.None);
        await store.CommitAsync(beta, "beta.txt", [new KnowledgeChunk("beta:old", 0, "old beta", 2, 0, 8, null, null)], [[0f, 1f]], options, CancellationToken.None);
        var replacements = new[]
        {
            new KnowledgePreparedIndex(alpha with { UpdatedUtc = now.AddMinutes(1) }, "alpha.txt", [new KnowledgeChunk("alpha:new", 0, "fresh alpha", 2, 0, 11, null, null)], [[1f, 0f]], "alpha:v2", new string('a', 64), new string('b', 64)),
            new KnowledgePreparedIndex(beta with { UpdatedUtc = now.AddMinutes(1) }, "beta.txt", [new KnowledgeChunk("beta:new", 0, "fresh beta", 2, 0, 10, null, null)], [[0f, 1f]], "beta:v2", new string('c', 64), new string('d', 64)),
        };

        await store.ReplaceAllIndexesAsync(replacements, options, CancellationToken.None);

        (await store.SearchLexicalCandidatesAsync(["public"], KnowledgeLexicalQuery.Create("old", options), 10, CancellationToken.None)).Should().BeEmpty();
        (await store.SearchLexicalCandidatesAsync(["public"], KnowledgeLexicalQuery.Create("fresh", options), 10, CancellationToken.None)).Select(candidate => candidate.ChunkId).Should().Equal("alpha:new", "beta:new");
    }

    /// <summary>Verifies concurrent readers see one complete active revision and matching FTS row during replacement.</summary>
    [Fact]
    public async Task ConcurrentReadersSeeAtomicFtsRevisionVisibility()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        var store = new SqliteKnowledgeStore(paths); await store.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        var options = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "test" };
        var original = new KnowledgeDocument("atomic", "Atomic", "", ["public"], "atomic.txt", "text/plain", 1, 1, 1, now);
        await store.CommitAsync(original, "atomic.txt", [new KnowledgeChunk("atomic:old", 0, "old marker", 2, 0, 10, null, null)], [[1f, 0f]], options, CancellationToken.None);
        var replacement = new KnowledgePreparedIndex(original with { UpdatedUtc = now.AddMinutes(1) }, "atomic.txt", [new KnowledgeChunk("atomic:new", 0, "new marker", 2, 0, 10, null, null)], [[0f, 1f]], "atomic:v2", new string('a', 64), new string('b', 64));
        var readers = Enumerable.Range(0, 3).Select(async _ =>
        {
            for (var index = 0; index < 20; index++)
            {
                await using var connection = new SqliteConnection($"Data Source={paths.KnowledgeDatabasePath};Pooling=False");
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT c.id,f.chunk_id FROM chunks c JOIN documents d ON d.id=c.document_id AND d.active_revision_id=c.revision_id JOIN chunk_fts f ON f.chunk_id=c.id AND f.revision_id=c.revision_id WHERE d.id='atomic';";
                await using var reader = await command.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                reader.GetString(0).Should().Be(reader.GetString(1));
                (await reader.ReadAsync()).Should().BeFalse();
            }
        });

        await Task.WhenAll(readers.Append(store.ReplaceAsync(replacement, options, CancellationToken.None)));
    }

    /// <summary>Verifies the prompt editor preserves exact aliases configured on a prompt step.</summary>
    [Fact]
    public void PromptEditorPreservesExactAliasSelection()
    {
        var viewModel = new WorkflowPromptEditorViewModel();
        viewModel.Initialize(new WorkflowPromptEditorRequest(
            """[{"step":"ai.prompt","toolAliases":["knowledge.search"],"args":{"prompt":"Search when useful."}}]"""));
        var item = viewModel.Items.Should().ContainSingle().Which;
        item.PromptText = "Search the authorized knowledge when useful.";
        viewModel.ApplyCommand.Execute(null);
        var workflow = WorkflowDefinitionSerializer.Deserialize(viewModel.Result!.WorkflowText);
        workflow.Should().ContainSingle().Which.ToolAliases.Should().Equal("knowledge.search");
    }

    /// <summary>Verifies the pre-release document-only schema migrates forward without losing its active index.</summary>
    [Fact]
    public async Task VersionOneKnowledgeSchemaMigratesForward()
    {
        var paths = new WorkspacePaths(_directory); paths.EnsureDirectories();
        SqliteNativeBootstrap.Initialize();
        await using (var connection = new SqliteConnection($"Data Source={paths.KnowledgeDatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);
                INSERT INTO metadata VALUES('embedding_provider','ollama'),('embedding_model','test'),('dimension','2'),('chunking_version','1'),('index_version','1'),('distance','cosine'),('normalization','unit-l2');
                CREATE TABLE documents(id TEXT PRIMARY KEY,name TEXT NOT NULL,description TEXT NOT NULL,original_file_name TEXT NOT NULL,media_type TEXT NOT NULL,size_bytes INTEGER NOT NULL,character_count INTEGER NOT NULL,chunk_count INTEGER NOT NULL,canonical_file_name TEXT NOT NULL,updated_utc TEXT NOT NULL);
                CREATE TABLE document_tags(document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,normalized_tag TEXT NOT NULL,PRIMARY KEY(document_id,normalized_tag));
                CREATE TABLE chunks(id TEXT PRIMARY KEY,document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,ordinal INTEGER NOT NULL,text TEXT NOT NULL,estimated_tokens INTEGER NOT NULL,start_character INTEGER NOT NULL,end_character INTEGER NOT NULL,heading TEXT NULL,page_number INTEGER NULL,UNIQUE(document_id,ordinal));
                CREATE TABLE vectors(chunk_id TEXT PRIMARY KEY REFERENCES chunks(id) ON DELETE CASCADE,dimension INTEGER NOT NULL,vector BLOB NOT NULL);
                INSERT INTO documents VALUES('a','Legacy','','a.txt','text/plain',8,8,1,'a.txt','2026-08-20T10:00:00.0000000+00:00');
                INSERT INTO document_tags VALUES('a','public');
                INSERT INTO chunks VALUES('a:0','a',0,'old text',2,0,8,NULL,NULL);
                INSERT INTO vectors VALUES('a:0',2,zeroblob(8));
                PRAGMA user_version=1;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteKnowledgeStore(paths);
        await store.InitializeAsync(CancellationToken.None);
        (await store.ListAsync(CancellationToken.None)).Should().ContainSingle().Which.Name.Should().Be("Legacy");
        (await store.GetCandidatesAsync(["public"], CancellationToken.None)).Should().ContainSingle().Which.Text.Should().Be("old text");
    }

    private static IOptionsMonitor<KnowledgeOptions> Options(IReadOnlyList<string> unused)
    {
        _ = unused; var options = new KnowledgeOptions { EmbeddingProvider = "Ollama", EmbeddingModel = "test", MinimumSimilarity = 0, MaximumResultTokens = 100 };
        return Mock.Of<IOptionsMonitor<KnowledgeOptions>>(monitor => monitor.CurrentValue == options);
    }

    private static KnowledgeLibraryService CreateLibrary(
        WorkspacePaths paths,
        IOptionsMonitor<KnowledgeOptions> options,
        IKnowledgeDocumentExtractor extractor,
        IKnowledgeEmbeddingService embeddings)
    {
        var estimator = new Mock<ITokenEstimator>();
        estimator.Setup(item => item.EstimateText(It.IsAny<string>())).Returns(5);
        return new KnowledgeLibraryService(
            paths,
            new SqliteKnowledgeStore(paths),
            extractor,
            new KnowledgeChunker(estimator.Object, options),
            embeddings,
            options,
            TimeProvider.System);
    }

    /// <inheritdoc />
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class RecordingEmbeddings(float[] vector) : IKnowledgeEmbeddingService
    {
        public int CallCount { get; private set; }
        public Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken) { CallCount++; return Task.FromResult<IReadOnlyList<float[]>>(inputs.Select(_ => vector).ToArray()); }
    }

    private sealed class MappingEmbeddings(IReadOnlyDictionary<string, float[]> vectors) : IKnowledgeEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>>(inputs.Select(input => vectors[input]).ToArray());
    }

    private sealed class RecordingProgress(Action<KnowledgeIndexProgress> report) : IProgress<KnowledgeIndexProgress>
    {
        public void Report(KnowledgeIndexProgress value) => report(value);
    }

    private sealed record KnowledgeEvaluationFixture(KnowledgeEvaluationDocument[] Documents, KnowledgeEvaluationCase[] Cases);
    private sealed record KnowledgeEvaluationDocument(string Id, string[] Tags, KnowledgeEvaluationChunk[] Chunks);
    private sealed record KnowledgeEvaluationChunk(string Id, int Ordinal, string Heading, int Page, string Text, float[] Vector);
    private sealed record KnowledgeEvaluationCase(string Id, string Query, float[] QueryVector, string[] Tags, string[] ExpectedChunkIds);
    private static float Dot(float[] left, float[] right) => left.Zip(right).Sum(pair => pair.First * pair.Second);

    private sealed class RecordingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<int> BatchSizes { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var inputs = values.ToArray();
            BatchSizes.Add(inputs.Length);
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                inputs.Select(_ => new Embedding<float>(new float[] { 3, 4 }))));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
