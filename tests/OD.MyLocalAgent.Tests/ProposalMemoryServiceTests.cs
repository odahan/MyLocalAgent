using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly JsonSerializerOptions SelectionJsonOptions = CreateSelectionJsonOptions();
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

    /// <summary>Preserves a true used flag when an idempotent retry supplies false.</summary>
    [Fact]
    public async Task StoreProposalNeverClearsUsedFlag()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, "Used proposal", "First", "https://example.com/used", true, CancellationToken.None);

        await service.StoreAsync(path, "Used proposal", "Retry", "https://example.com/used", false, CancellationToken.None);

        await using var connection = Open(path, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT used FROM editorial_proposals";
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(1);
    }

    /// <summary>Rejects model-invented source identifiers before reserving or exporting anything.</summary>
    [Fact]
    public async Task ReserveSelectionRejectsUnknownSourceId()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();

        var action = () => service.ReserveSelectionAsync(
            path, "unknown-source", Discovery(("source-1", "https://example.com/one")),
            Selection(Proposal("p1", "Subject", "missing")), 6, 0.92, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unknown*source id*");
        (await ScalarAsync(path, "SELECT count(*) FROM editorial_runs")).Should().Be(0L);
    }

    /// <summary>Accepts the string enum representation required from workflow AI responses.</summary>
    [Fact]
    public async Task ReserveSelectionAcceptsWorkflowStringNoveltyDecision()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        var selection = Selection(Proposal("p1", "Subject", "source-1"));

        selection.Should().Contain("\"noveltyDecision\":\"New\"");
        var result = await service.ReserveSelectionAsync(
            path, "string-enum", Discovery(("source-1", "https://example.com/one")),
            selection, 6, 0.92, CancellationToken.None);

        result.Approved.Should().ContainSingle();
    }

    /// <summary>Rejects numeric enum values because the workflow contract exposes named decisions.</summary>
    [Fact]
    public async Task ReserveSelectionRejectsNumericNoveltyDecision()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        var selection = Selection(Proposal("p1", "Subject", "source-1"))
            .Replace("\"noveltyDecision\":\"New\"", "\"noveltyDecision\":0", StringComparison.Ordinal);

        var action = () => service.ReserveSelectionAsync(
            path, "numeric-enum", Discovery(("source-1", "https://example.com/one")),
            selection, 6, 0.92, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.WithMessage("*selection JSON does not satisfy its contract*");
        exception.Which.InnerException.Should().BeOfType<JsonException>();
        (await ScalarAsync(path, "SELECT count(*) FROM editorial_runs")).Should().Be(0L);
    }

    /// <summary>Rejects the same announcement from two sites inside one selection batch.</summary>
    [Fact]
    public async Task ReserveSelectionDeduplicatesCurrentBatchSemantically()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        var selection = Selection(
            Proposal("union-register", "C# union types arrive", "register"),
            Proposal("union-lavx", "Les types union arrivent en C#", "lavx"));

        var result = await service.ReserveSelectionAsync(
            path, "batch", Discovery(
                ("register", "https://theregister.com/csharp-union-types"),
                ("lavx", "https://lavx.hu/csharp-union-types")),
            selection, 6, 0.92, CancellationToken.None);

        result.Approved.Should().ContainSingle();
        result.Rejected.Should().ContainSingle().Which.Decision.Should().Be(EditorialNoveltyDecision.Duplicate);
    }

    /// <summary>Rejects a repeated announcement even when tracking parameters alter its supplied URL.</summary>
    [Fact]
    public async Task ReserveSelectionRejectsCanonicalHistoricalUrlBeyondSemanticWindow()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, ".NET 11 NativeAOT", "Preview 7 announcement", "https://byteiota.com/net-11-preview-7?utm_source=first", false, CancellationToken.None);
        await SetCreatedAtAsync(path, "2020-01-01T00:00:00.0000000+00:00");

        var result = await service.ReserveSelectionAsync(
            path, "old-exact", Discovery(("native", "https://byteiota.com/net-11-preview-7?utm_campaign=again")),
            Selection(Proposal("native-repeat", "NativeAOT devient la valeur par défaut", "native")),
            1, 0.92, CancellationToken.None);

        result.Approved.Should().BeEmpty();
        result.Rejected.Should().ContainSingle().Which.Decision.Should().Be(EditorialNoveltyDecision.Duplicate);
    }

    /// <summary>Allows a known release page only when a distinct dated/versioned material update is declared.</summary>
    [Fact]
    public async Task ReserveSelectionAllowsSupportedMaterialUpdateOnKnownReleasePage()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, "Product 1.0", "Version one", "https://example.com/releases", false, CancellationToken.None);
        var update = Proposal("v2", "Product 2.0", "releases") with
        {
            NoveltyDecision = EditorialNoveltyDecision.MaterialUpdate,
            EventVersion = "2.0",
            EventDate = new DateOnly(2026, 8, 30),
            ClaimedNewFacts = [new TestFact("Version 2.0 fixes a distinct startup defect.", ["releases"])],
        };

        var result = await service.ReserveSelectionAsync(
            path, "material", Discovery(("releases", "https://example.com/releases")),
            Selection(update), 6, 0.92, CancellationToken.None);

        result.Approved.Should().ContainSingle().Which.Decision.Should().Be(EditorialNoveltyDecision.MaterialUpdate);
    }

    /// <summary>Fails closed when historical semantic coverage is incomplete.</summary>
    [Fact]
    public async Task ReserveSelectionMarksIncompatibleHistoryUncertain()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, "Stored", "History", "https://example.com/history", false, CancellationToken.None);
        await ExecuteAsync(path, "UPDATE editorial_proposals SET embedding_model = 'obsolete-model'");

        var result = await service.ReserveSelectionAsync(
            path, "incompatible", Discovery(("new", "https://example.com/new")),
            Selection(Proposal("new", "Different subject", "new")), 6, 0.92, CancellationToken.None);

        result.Approved.Should().BeEmpty();
        result.IncompatibleEmbeddingCount.Should().Be(1);
        result.Rejected.Should().ContainSingle().Which.Decision.Should().Be(EditorialNoveltyDecision.Uncertain);
    }

    /// <summary>Does not report incompatible history when an empty selection requires no semantic comparison.</summary>
    [Fact]
    public async Task EmptySelectionDoesNotReportIrrelevantEmbeddingIncompatibilities()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, "Stored", "History", "https://example.com/history", false, CancellationToken.None);

        var result = await service.ReserveSelectionAsync(
            path, "empty", Discovery(("candidate", "https://example.com/candidate")),
            Selection(), 6, 0.92, CancellationToken.None);

        result.Approved.Should().BeEmpty();
        result.Rejected.Should().BeEmpty();
        result.EmbeddingDimension.Should().Be(0);
        result.IncompatibleEmbeddingCount.Should().Be(0);
    }

    /// <summary>Allows the workflow to persist an empty run when discovery delivered no candidates.</summary>
    [Fact]
    public async Task EmptySelectionCanReserveAnIncompleteDiscovery()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();

        var result = await service.ReserveSelectionAsync(
            path, "empty-rate-limited", DiscoveryWithStatus("rateLimited"),
            Selection(), 6, 0.92, CancellationToken.None);

        result.Approved.Should().BeEmpty();
        result.Rejected.Should().BeEmpty();
        (await ScalarAsync(path, "SELECT count(*) FROM editorial_runs")).Should().Be(1L);
    }

    /// <summary>Allows verified delivered candidates to proceed when the overall search was only partial.</summary>
    [Fact]
    public async Task DeliveredCandidateCanProceedFromDegradedDiscovery()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();

        var result = await service.ReserveSelectionAsync(
            path, "degraded-candidate", DiscoveryWithStatus("degraded", ("source", "https://example.com/new")),
            Selection(Proposal("p1", "A new partial-search result", "source")), 6, 0.92, CancellationToken.None);

        result.Approved.Should().ContainSingle();
    }

    /// <summary>Persists before rendering and confirms only the exact prepared report bytes.</summary>
    [Fact]
    public async Task ReservationPreparationAndExportConfirmationAreIdempotent()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        var reservation = await service.ReserveSelectionAsync(
            path, "pipeline", Discovery(("source", "https://example.com/new")),
            Selection(Proposal("p1", "A genuinely new event", "source")), 6, 0.92, CancellationToken.None);
        var retry = await service.ReserveSelectionAsync(
            path, "pipeline", Discovery(("source", "https://example.com/new")),
            Selection(Proposal("p1", "A genuinely new event", "source")), 6, 0.92, CancellationToken.None);
        var unavailableRetry = await new ProposalMemoryService(
            new FailingEmbeddingService(),
            new MutableOptionsMonitor<KnowledgeOptions>(new KnowledgeOptions { EmbeddingModel = "proposal-model" }),
            _time).ReserveSelectionAsync(
                path, "pipeline", Discovery(("source", "https://example.com/new")),
                Selection(Proposal("p1", "A genuinely new event", "source")), 6, 0.92, CancellationToken.None);

        reservation.Approved.Should().ContainSingle();
        retry.Approved.Single().ProposalId.Should().Be(reservation.Approved.Single().ProposalId);
        unavailableRetry.Approved.Single().ProposalId.Should().Be(reservation.Approved.Single().ProposalId);
        var report = await service.PrepareReportAsync(path, "pipeline", Drafts("p1"), "Veille éditoriale — 30/08/2026", CancellationToken.None);
        var reportPath = Path.Combine(_root, "report.md");
        await File.WriteAllTextAsync(reportPath, report.Markdown);
        var exported = await service.ConfirmExportAsync(path, "pipeline", reportPath, CancellationToken.None);
        var exportedRetry = await service.ConfirmExportAsync(path, "pipeline", reportPath, CancellationToken.None);

        exported.Status.Should().Be("Exported");
        exportedRetry.Sha256.Should().Be(report.Sha256);
        (await ScalarAsync(path, "SELECT count(*) FROM editorial_proposals")).Should().Be(1L);
    }

    /// <summary>Inventories historical reports without writes, preserves historical dates, and is idempotent on apply.</summary>
    [Fact]
    public async Task HistoricalImportSupportsDryRunAndIdempotentApply()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        var reports = Path.Combine(_root, "reports");
        Directory.CreateDirectory(reports);
        await File.WriteAllTextAsync(Path.Combine(reports, "VeilleAuto-20260801.md"), """
            # Veille éditoriale — 01/08/2026

            ## Sujet 1 — Historical release

            A historical release introduced a specific capability.

            ### Sources
            - https://example.com/historical?utm_source=archive

            ## Diagnostic de collecte
            This section is not a subject.
            """);

        var dryRun = await service.ImportHistoryAsync(path, reports, true, CancellationToken.None);
        (await ScalarAsync(path, "SELECT count(*) FROM editorial_proposals")).Should().Be(0L);
        dryRun.SubjectsFound.Should().Be(1);
        dryRun.ImportedCount.Should().Be(0);

        var applied = await service.ImportHistoryAsync(path, reports, false, CancellationToken.None);
        var second = await service.ImportHistoryAsync(path, reports, false, CancellationToken.None);

        applied.ImportedCount.Should().Be(1);
        second.ImportedCount.Should().Be(0);
        second.ExistingCount.Should().Be(1);
        (await ScalarAsync(path, "SELECT count(*) FROM editorial_proposals")).Should().Be(1L);
        await using var connection = Open(path, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT substr(created_at_utc, 1, 10), canonical_url FROM editorial_proposals JOIN editorial_proposal_sources ON proposal_id=id";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("2026-08-01");
        reader.GetString(1).Should().Be("https://example.com/historical");
    }

    /// <summary>Serializes final novelty validation so concurrent runs cannot reserve the same event twice.</summary>
    [Fact]
    public async Task ConcurrentRunsCannotDoubleReserveTheSameEvent()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        var discovery = Discovery(("copilot", "https://github.blog/changelog/2026-08-28-github-copilot-in-visual-studio-august-update-2/"));
        var selection = Selection(Proposal("copilot", "GitHub Copilot Visual Studio August update", "copilot"));

        var results = await Task.WhenAll(
            service.ReserveSelectionAsync(path, "concurrent-a", discovery, selection, 6, 0.92, CancellationToken.None),
            service.ReserveSelectionAsync(path, "concurrent-b", discovery, selection, 6, 0.92, CancellationToken.None));

        results.Sum(static result => result.Approved.Count).Should().Be(1);
        results.Sum(static result => result.Rejected.Count).Should().Be(1);
        (await ScalarAsync(path, "SELECT count(*) FROM editorial_proposals")).Should().Be(1L);
    }

    /// <summary>Rejects a structurally mapped draft when its content is not aligned with the approved subject.</summary>
    [Fact]
    public async Task PrepareReportRejectsSemanticallyUnrelatedDraft()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "alignment.db");
        var service = new ProposalMemoryService(
            new StubEmbeddingService(input => input.StartsWith("UNRELATED", StringComparison.Ordinal) ? [0f, 1f] : [1f, 0f]),
            new MutableOptionsMonitor<KnowledgeOptions>(new KnowledgeOptions { EmbeddingModel = "proposal-model" }),
            _time);
        await service.CreateDatabaseAsync(path, CancellationToken.None);
        await service.ReserveSelectionAsync(path, "alignment", Discovery(("source", "https://example.com/aligned")),
            Selection(Proposal("aligned", "Approved runtime event", "source")), 6, 0.92, CancellationToken.None);
        var drafts = System.Text.Json.JsonSerializer.Serialize(new
        {
            subjects = new[] { new { temporaryId = "aligned", whyNow = "UNRELATED cooking recipe", criticalAngle = "Kitchen tools", body = "Ingredients", linkedInPost = "Dinner" } },
        });

        var action = () => service.PrepareReportAsync(path, "alignment", drafts, "Report", CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not semantically aligned*");
        (await ScalarAsync(path, "SELECT count(*) FROM editorial_runs WHERE status='ReadyToExport'")).Should().Be(0L);
    }

    /// <summary>Rebuilds incompatible embeddings explicitly and reports no remaining work on a second pass.</summary>
    [Fact]
    public async Task EditorialEmbeddingRebuildSupportsDryRunAndSecondPass()
    {
        var (service, path) = await CreateCurrentDatabaseAsync();
        await service.StoreAsync(path, "Legacy projection", "Stored summary", "https://example.com/legacy", false, CancellationToken.None);
        await ExecuteAsync(path, "UPDATE editorial_proposals SET embedding_model='obsolete-model'");

        var dryRun = await service.RebuildEmbeddingsAsync(path, true, CancellationToken.None);
        var applied = await service.RebuildEmbeddingsAsync(path, false, CancellationToken.None);
        var second = await service.RebuildEmbeddingsAsync(path, true, CancellationToken.None);

        dryRun.ConsideredCount.Should().Be(1);
        dryRun.RebuiltCount.Should().Be(0);
        applied.RebuiltCount.Should().Be(1);
        second.ConsideredCount.Should().Be(0);
        await using var connection = Open(path, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT p.embedding_model,m.projection_version FROM editorial_proposals p JOIN editorial_proposal_metadata m ON m.proposal_id=p.id";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("proposal-model");
        reader.GetString(1).Should().Be("editorial-rebuild-v2");
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

    private static async Task<long> ScalarAsync(string path, string sql)
    {
        await using var connection = Open(path, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string Discovery(params (string Id, string Url)[] sources) => System.Text.Json.JsonSerializer.Serialize(new
    {
        searchStatus = "available",
        candidates = sources.Select(static source => new { hitId = source.Id, canonicalUrl = source.Url }),
    });

    private static string DiscoveryWithStatus(string searchStatus, params (string Id, string Url)[] sources) => System.Text.Json.JsonSerializer.Serialize(new
    {
        searchStatus,
        candidates = sources.Select(static source => new { hitId = source.Id, canonicalUrl = source.Url }),
    });

    private static string Selection(params TestProposal[] proposals) => JsonSerializer.Serialize(new { proposals }, SelectionJsonOptions);

    private static JsonSerializerOptions CreateSelectionJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter<EditorialNoveltyDecision>(namingPolicy: null, allowIntegerValues: false));
        return options;
    }

    private static TestProposal Proposal(string temporaryId, string title, string sourceId) => new(
        temporaryId, title, "A concise source-backed factual summary.", [sourceId], ["C#"], null, null,
        [new TestFact("A source-backed announcement has been made.", [sourceId])], EditorialNoveltyDecision.New, "fr");

    private static string Drafts(string temporaryId) => System.Text.Json.JsonSerializer.Serialize(new
    {
        subjects = new[]
        {
            new
            {
                temporaryId,
                whyNow = "The source establishes why this event matters now.",
                criticalAngle = "The benefits and limits both require attention.",
                body = "Teams should verify the trade-offs and unresolved questions.",
                linkedInPost = "A new event deserves a measured look. It offers benefits but also constraints. What would you validate first? #dotnet #software",
            },
        },
    });

    private sealed record TestProposal(
        string TemporaryId,
        string Title,
        string FactualSummary,
        IReadOnlyList<string> SourceIds,
        IReadOnlyList<string> Entities,
        string? EventVersion,
        DateOnly? EventDate,
        IReadOnlyList<TestFact> ClaimedNewFacts,
        EditorialNoveltyDecision NoveltyDecision,
        string Language);

    private sealed record TestFact(string Text, IReadOnlyList<string> SourceIds);

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
