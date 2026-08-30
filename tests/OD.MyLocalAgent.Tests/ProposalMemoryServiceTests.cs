using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the task-owned editorial proposal-memory contract.</summary>
public sealed class ProposalMemoryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"od-proposal-memory-{Guid.NewGuid():N}");
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Creates the current schema idempotently and upgrades the supported legacy schema atomically.</summary>
    [Fact]
    public async Task CreateDatabaseIsIdempotentAndMigratesLegacyRows()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "memory.db");
        await using (var connection = Open(path, SqliteOpenMode.ReadWriteCreate))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE editorial_proposals (
                    id INTEGER PRIMARY KEY, title TEXT NOT NULL, summary TEXT NOT NULL,
                    source_url TEXT NULL, created_at_utc TEXT NOT NULL,
                    used INTEGER NOT NULL DEFAULT 0 CHECK (used IN (0, 1)));
                INSERT INTO editorial_proposals
                    (title, summary, source_url, created_at_utc, used)
                VALUES ('Legacy title', 'Legacy summary', NULL, '2026-08-30T10:00:00.0000000+00:00', 0);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var service = CreateService();
        var first = await service.CreateDatabaseAsync(path, CancellationToken.None);
        var second = await service.CreateDatabaseAsync(path, CancellationToken.None);

        first.TableCreated.Should().BeFalse();
        second.SchemaCompatible.Should().BeTrue();
        await using var verify = Open(path, SqliteOpenMode.ReadOnly);
        await verify.OpenAsync();
        var commandVerify = verify.CreateCommand();
        commandVerify.CommandText = "SELECT proposal_key, embedding_model, embedding_dimension, length(embedding) FROM editorial_proposals";
        await using var reader = await commandVerify.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().HaveLength(64);
        reader.GetString(1).Should().Be("proposal-model");
        reader.GetInt32(2).Should().Be(2);
        reader.GetInt32(3).Should().Be(8);
    }

    /// <summary>Stores one vector and turns an identical retry into an idempotent update.</summary>
    [Fact]
    public async Task StoreProposalCalculatesEmbeddingAndIsIdempotent()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();

        var first = await service.StoreAsync(path, "  A proposal! ", "First summary", "https://EXAMPLE.com/post?utm_source=x", false, CancellationToken.None);
        var second = await service.StoreAsync(path, "A proposal", "Updated summary", "https://example.com/post", true, CancellationToken.None);

        first.Inserted.Should().BeTrue();
        second.Inserted.Should().BeFalse();
        second.ProposalKey.Should().Be(first.ProposalKey);
        await using var connection = Open(path, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*), summary, used, embedding_dimension, length(embedding) FROM editorial_proposals";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt32(0).Should().Be(1);
        reader.GetString(1).Should().Be("Updated summary");
        reader.GetInt32(2).Should().Be(1);
        reader.GetInt32(3).Should().Be(2);
        reader.GetInt32(4).Should().Be(8);
    }

    /// <summary>Rejects exact normalized title and canonical URL matches before embedding comparison.</summary>
    [Fact]
    public async Task FilterRejectsRecentExactTitleAndUrlMatches()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, "C# — performance", "Stored", "https://example.com/source?utm_campaign=x", false, CancellationToken.None);
        var candidates = new[]
        {
            Document("title", " C# performance! ", "https://elsewhere.test/a"),
            Document("same url", "Different title", "https://EXAMPLE.com/source"),
        };

        var result = await service.FilterAsync(path, candidates, 6, false, 0.92, CancellationToken.None);

        result.Candidates.Should().BeEmpty();
        result.RejectedExactCount.Should().Be(2);
        result.RejectedSemanticCount.Should().Be(0);
        result.ConsideredCount.Should().Be(1);
    }

    /// <summary>Uses exact local cosine comparison at the configured threshold.</summary>
    [Fact]
    public async Task FilterRejectsAtThresholdAndRetainsBelowThreshold()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, "Stored proposal", "Stored", null, false, CancellationToken.None);
        var candidates = new[]
        {
            Document("threshold", "Threshold candidate", "https://example.com/threshold"),
            Document("below", "Below candidate", "https://example.com/below"),
        };

        var result = await service.FilterAsync(path, candidates, 6, false, 0.92, CancellationToken.None);

        result.Candidates.Should().ContainSingle().Which.HitId.Should().Be("below");
        result.RejectedSemanticCount.Should().Be(1);
    }

    /// <summary>Applies age independently from the optional used-tag rule.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task OldUsedProposalDependsOnUsedTag(bool useUsedTag, bool candidateRetained)
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, "Old used", "Stored", null, true, CancellationToken.None);
        await SetCreatedAtAsync(path, "2025-01-01T00:00:00.0000000+00:00");

        var result = await service.FilterAsync(
            path,
            [Document("candidate", "Old used", "https://example.com/new")],
            6,
            useUsedTag,
            0.92,
            CancellationToken.None);

        result.Candidates.Any().Should().Be(candidateRetained);
    }

    /// <summary>Treats the exact UTC cutoff as recent.</summary>
    [Fact]
    public async Task CutoffBoundaryIsIncluded()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, "Boundary", "Stored", null, false, CancellationToken.None);
        await SetCreatedAtAsync(path, "2026-02-28T12:00:00.0000000+00:00");

        var result = await service.FilterAsync(path, [Document("candidate", "Boundary", "https://example.com/b")], 6, false, 0.92, CancellationToken.None);

        result.RejectedExactCount.Should().Be(1);
    }

    /// <summary>Reports but never compares vectors from another model.</summary>
    [Fact]
    public async Task IncompatibleEmbeddingIsReportedAndCandidateIsRetained()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, "Stored proposal", "Stored", null, false, CancellationToken.None);
        await ExecuteAsync(path, "UPDATE editorial_proposals SET embedding_model = 'another-model'");

        var result = await service.FilterAsync(path, [Document("candidate", "Threshold candidate", "https://example.com/c")], 6, false, 0.92, CancellationToken.None);

        result.Candidates.Should().ContainSingle();
        result.IncompatibleEmbeddingCount.Should().Be(1);
        result.RejectedSemanticCount.Should().Be(0);
    }

    /// <summary>Fails instead of weakening semantic filtering when embedding generation is unavailable.</summary>
    [Fact]
    public async Task FilterFailsWhenEmbeddingCapabilityIsUnavailable()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, "Stored proposal", "Stored", null, false, CancellationToken.None);
        var unavailable = new ProposalMemoryService(
            new FailingEmbeddingService(),
            new MutableOptionsMonitor<KnowledgeOptions>(new KnowledgeOptions { EmbeddingModel = "proposal-model" }),
            _time);

        var action = () => unavailable.FilterAsync(
            path,
            [Document("candidate", "Different proposal", "https://example.com/different")],
            6,
            false,
            0.92,
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Local embedding unavailable.");
    }

    /// <summary>Rejects non-local embedding configuration before invoking an embedding generator.</summary>
    [Fact]
    public async Task StoreRejectsExternalEmbeddingProviderBeforeGeneration()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "local-only.db");
        var local = CreateService();
        await local.CreateDatabaseAsync(path, CancellationToken.None);
        var embedding = new CountingEmbeddingService();
        var service = new ProposalMemoryService(
            embedding,
            new MutableOptionsMonitor<KnowledgeOptions>(new KnowledgeOptions
            {
                EmbeddingProvider = "OpenAI",
                EmbeddingModel = "external-model",
            }),
            _time);

        var action = () => service.StoreAsync(path, "Title", "Summary", null, false, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Proposal memory requires the local Ollama embedding provider.");
        embedding.CallCount.Should().Be(0);
    }

    /// <summary>Fails clearly when the application table is absent or incompatible.</summary>
    [Fact]
    public async Task FilterRejectsMissingApplicationSchema()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "invalid.db");
        await using (var connection = Open(path, SqliteOpenMode.ReadWriteCreate))
        {
            await connection.OpenAsync();
        }

        var action = () => CreateService().FilterAsync(path, [], 6, false, 0.92, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<(ProposalMemoryService Service, string Path)> CreateCurrentDatabaseAsync()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"memory-{Guid.NewGuid():N}.db");
        var service = CreateService();
        await service.CreateDatabaseAsync(path, CancellationToken.None);
        return (service, path);
    }

    private ProposalMemoryService CreateService() => new(
        new StubEmbeddingService(input => input.StartsWith("Threshold candidate", StringComparison.Ordinal)
            ? [0.92f, MathF.Sqrt(1 - (0.92f * 0.92f))]
            : input.StartsWith("Below candidate", StringComparison.Ordinal)
                ? [0.91f, MathF.Sqrt(1 - (0.91f * 0.91f))]
                : [1f, 0f]),
        new MutableOptionsMonitor<KnowledgeOptions>(new KnowledgeOptions { EmbeddingModel = "proposal-model" }),
        _time);

    private static WebDocument Document(string id, string title, string url) => new(
        id, url, url, url, title, "example.com", WebDocumentKind.Article,
        DateTimeOffset.Parse("2026-08-30T10:00:00+00:00"), PublicationDateStatus.Verified,
        "Candidate summary", "text/html", ["query"], DateTimeOffset.Parse("2026-08-30T11:00:00+00:00"));

    private static SqliteConnection Open(string path, SqliteOpenMode mode) => new(
        new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());

    private static async Task SetCreatedAtAsync(string path, string value)
    {
        await using var connection = Open(path, SqliteOpenMode.ReadWrite);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE editorial_proposals SET created_at_utc = $value";
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = Open(path, SqliteOpenMode.ReadWrite);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class StubEmbeddingService(Func<string, float[]> selector) : IKnowledgeEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>>(inputs.Select(selector).ToArray());
    }

    private sealed class FailingEmbeddingService : IKnowledgeEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<float[]>>(new InvalidOperationException("Local embedding unavailable."));
    }

    private sealed class CountingEmbeddingService : IKnowledgeEmbeddingService
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<float[]>>([[1f, 0f]]);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
