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
        var service = new KnowledgeSearchService(store, embeddings, options);
        var response = await service.SearchAsync("query", ["public"], null, 5, CancellationToken.None);
        response.Results.Should().ContainSingle().Which.DocumentId.Should().Be("a");
        var denied = async () => await service.SearchAsync("query", [], null, 5, CancellationToken.None);
        await denied.Should().ThrowAsync<KnowledgeAuthorizationException>();
        embeddings.CallCount.Should().Be(1);
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
        (await command.ExecuteScalarAsync()).Should().Be(3L);
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
        (await store.FindActiveBySourceHashAsync(new string('a', 64), CancellationToken.None))!.Id.Should().Be("a");
        (await store.GetChunkPreviewAsync("a", 10, 1000, CancellationToken.None)).Should().ContainSingle().Which.Text.Should().Be("new text");
    }

    /// <summary>Verifies the prompt editor derives exact choices from the catalog and enabled task groups.</summary>
    [Fact]
    public void PromptEditorUsesCatalogDrivenExactAliasSelection()
    {
        var viewModel = new WorkflowPromptEditorViewModel(ToolCatalog.Default);
        viewModel.Initialize(new WorkflowPromptEditorRequest(
            """[{"step":"ai.prompt","args":{"prompt":"Search when useful."}}]""",
            [ToolGroups.Knowledge], ["public"]));
        var item = viewModel.Items.Should().ContainSingle().Which;
        item.ToolSelections.Should().ContainSingle().Which.Alias.Should().Be("knowledge.search");
        item.ToolSelections[0].IsSelected = true;
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
