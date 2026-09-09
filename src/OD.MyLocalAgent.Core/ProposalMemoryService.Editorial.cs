using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace OD.MyLocalAgent.Core;

public sealed partial class ProposalMemoryService
{
    internal const string EditorialProjectionVersion = "editorial-event-v2";
    private const double DraftAlignmentThreshold = 0.50;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex AbsoluteUrlRegex = new(@"https?://[^\s\)\]>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions EditorialJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter<EditorialNoveltyDecision>(namingPolicy: null, allowIntegerValues: false) },
    };

    /// <inheritdoc />
    public async Task<EditorialReservationResult> ReserveSelectionAsync(
        string databasePath,
        string runId,
        string discoveryJson,
        string selectionJson,
        int lookbackMonths,
        double semanticSimilarityThreshold,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (lookbackMonths is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(lookbackMonths));
        }

        if (semanticSimilarityThreshold is < 0.80 or > 0.99 || !double.IsFinite(semanticSimilarityThreshold))
        {
            throw new ArgumentOutOfRangeException(nameof(semanticSimilarityThreshold));
        }

        var discovery = ParseDiscovery(discoveryJson);
        var selection = ParseSelection(selectionJson);
        if (selection.Proposals.Count > 3)
        {
            throw new EditorialContractException("Editorial selection accepts at most three proposals.");
        }

        ValidateSelection(selection, discovery);
        await using var reservationLock = await AcquireReservationLockAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var priorReservation = await Task.Run(() => ReadReservationFromDatabase(databasePath, runId.Trim(), cancellationToken), cancellationToken).ConfigureAwait(false);
        if (priorReservation is not null)
        {
            return priorReservation;
        }

        var projections = selection.Proposals.Select(Projection).ToArray();
        var model = GetEmbeddingModel();
        var vectors = projections.Length == 0
            ? Array.Empty<float[]>()
            : (await _embeddingService.GenerateAsync(projections, cancellationToken).ConfigureAwait(false)).ToArray();
        if (vectors.Length != projections.Length || vectors.Any(static vector => vector.Length == 0))
        {
            throw new InvalidOperationException("The embedding provider returned an incomplete editorial selection projection.");
        }

        var dimension = vectors.FirstOrDefault()?.Length ?? 0;
        if (vectors.Any(vector => vector.Length != dimension))
        {
            throw new InvalidOperationException("The embedding provider returned inconsistent editorial selection dimensions.");
        }

        return await Task.Run(() => ReserveSelectionCore(
            databasePath,
            runId.Trim(),
            discovery,
            selection,
            vectors,
            model,
            dimension,
            lookbackMonths,
            semanticSimilarityThreshold,
            cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EditorialReportResult> PrepareReportAsync(
        string databasePath,
        string runId,
        string draftsJson,
        string reportTitle,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportTitle);
        var drafts = ParseDrafts(draftsJson);
        var model = drafts.Subjects.Count == 0 ? string.Empty : GetEmbeddingModel();
        var draftVectors = drafts.Subjects.Count == 0
            ? Array.Empty<float[]>()
            : (await _embeddingService.GenerateAsync(drafts.Subjects.Select(DraftProjection).ToArray(), cancellationToken).ConfigureAwait(false)).ToArray();
        if (draftVectors.Length != drafts.Subjects.Count || draftVectors.Any(static vector => vector.Length == 0))
        {
            throw new InvalidOperationException("The embedding provider returned an incomplete editorial draft projection.");
        }
        await using var reservationLock = await AcquireReservationLockAsync(databasePath, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => PrepareReportCore(databasePath, runId.Trim(), drafts, draftVectors, model, reportTitle.Trim(), cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EditorialExportResult> ConfirmExportAsync(
        string databasePath,
        string runId,
        string reportPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        await using var reservationLock = await AcquireReservationLockAsync(databasePath, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => ConfirmExportCore(databasePath, runId.Trim(), reportPath, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private EditorialReservationResult ReserveSelectionCore(
        string databasePath,
        string runId,
        DiscoveryEnvelope discovery,
        SelectionEnvelope selection,
        IReadOnlyList<float[]> vectors,
        string model,
        int dimension,
        int lookbackMonths,
        double threshold,
        CancellationToken cancellationToken)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        ValidateSchema(connection, null);
        EnsureEditorialSchema(connection, null);
        var existing = ReadReservation(connection, runId);
        if (existing is not null)
        {
            return existing;
        }

        var history = ReadEditorialHistory(connection);
        var cutoff = _timeProvider.GetUtcNow().AddMonths(-lookbackMonths);
        var decisions = new List<CandidateDecision>(selection.Proposals.Count);
        var acceptedVectors = new List<float[]>();
        var incompatibleCount = selection.Proposals.Count == 0
            ? 0
            : history.Count(item => item.CreatedAtUtc >= cutoff &&
                (!string.Equals(item.EmbeddingModel, model, StringComparison.Ordinal) ||
                 item.EmbeddingDimension != dimension || item.Embedding.Length != dimension));

        for (var index = 0; index < selection.Proposals.Count; index++)
        {
            var proposal = selection.Proposals[index];
            var sources = proposal.SourceIds.Select(id => new EditorialSource(id, discovery.Candidates[id])).ToArray();
            var eventFingerprint = EventFingerprint(proposal);
            var decision = Decide(proposal, sources, eventFingerprint, vectors[index], history, acceptedVectors, cutoff, model, dimension, threshold, incompatibleCount);
            decisions.Add(decision);
            if (decision.Approved)
            {
                acceptedVectors.Add(vectors[index]);
            }
        }

        using var transaction = connection.BeginTransaction();
        InsertRun(connection, transaction, runId, selection.Proposals.Count, decisions.Count(static item => item.Approved), decisions.Count(static item => !item.Approved), model, dimension, incompatibleCount);
        var approved = new List<ReservedEditorialProposal>();
        var rejected = new List<RejectedEditorialProposal>();
        for (var decisionIndex = 0; decisionIndex < decisions.Count; decisionIndex++)
        {
            var decision = decisions[decisionIndex];
            if (!decision.Approved)
            {
                InsertRunDecision(connection, transaction, runId, decision.Proposal.TemporaryId, null, decision.Decision, decision.Reason, decision.HistoricalProposalId, decision.Similarity);
                rejected.Add(new RejectedEditorialProposal(decision.Proposal.TemporaryId, decision.Decision, decision.Reason, decision.HistoricalProposalId, decision.Similarity));
                continue;
            }

            var proposalId = InsertReservedProposal(connection, transaction, decision, model, dimension, vectors[decisionIndex]);
            InsertRunDecision(connection, transaction, runId, decision.Proposal.TemporaryId, proposalId, decision.Decision, decision.Reason, decision.HistoricalProposalId, decision.Similarity);
            approved.Add(new ReservedEditorialProposal(
                decision.Proposal.TemporaryId,
                proposalId,
                decision.Proposal.Title.Trim(),
                decision.Proposal.FactualSummary.Trim(),
                decision.Sources,
                decision.Decision));
        }

        transaction.Commit();
        return new EditorialReservationResult(runId, "Reserved", approved, rejected, model, dimension, EditorialProjectionVersion, incompatibleCount);
    }

    private static EditorialReportResult PrepareReportCore(
        string databasePath,
        string runId,
        DraftEnvelope drafts,
        IReadOnlyList<float[]> draftVectors,
        string embeddingModel,
        string reportTitle,
        CancellationToken cancellationToken)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        EnsureEditorialSchema(connection, null);
        var proposals = ReadApprovedForReport(connection, runId);
        var draftById = drafts.Subjects.ToDictionary(static item => item.TemporaryId, StringComparer.Ordinal);
        if (draftById.Count != drafts.Subjects.Count || draftById.Count != proposals.Count || proposals.Any(item => !draftById.ContainsKey(item.TemporaryId)))
        {
            throw new InvalidOperationException("The editorial draft must contain exactly one block for every approved proposal and no other block.");
        }

        var allowedUrls = proposals.SelectMany(static item => item.Sources).Select(static item => item.CanonicalUrl).ToHashSet(StringComparer.Ordinal);
        for (var draftIndex = 0; draftIndex < drafts.Subjects.Count; draftIndex++)
        {
            var draft = drafts.Subjects[draftIndex];
            var proposal = proposals.Single(item => item.TemporaryId == draft.TemporaryId);
            if (!string.Equals(proposal.EmbeddingModel, embeddingModel, StringComparison.Ordinal) ||
                proposal.Embedding.Length != draftVectors[draftIndex].Length ||
                Cosine(proposal.Embedding, draftVectors[draftIndex]) < DraftAlignmentThreshold)
            {
                throw new InvalidOperationException($"Draft '{draft.TemporaryId}' is not semantically aligned with its approved proposal.");
            }
            foreach (Match match in AbsoluteUrlRegex.Matches(string.Join('\n', draft.WhyNow, draft.CriticalAngle, draft.Body, draft.LinkedInPost)))
            {
                var normalized = NormalizeUrl(match.Value.TrimEnd('.', ',', ';', ':'));
                if (normalized is not null && !allowedUrls.Contains(normalized))
                {
                    throw new InvalidOperationException($"Draft '{draft.TemporaryId}' contains an unapproved source URL.");
                }
            }
        }

        var markdown = BuildMarkdown(reportTitle, proposals, draftById);
        var hash = Sha256(markdown);
        using var transaction = connection.BeginTransaction();
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE editorial_runs SET status = 'ReadyToExport', report_markdown = $markdown, report_sha256 = $hash WHERE run_id = $runId AND status IN ('Reserved', 'ReadyToExport')";
        update.Parameters.AddWithValue("$markdown", markdown);
        update.Parameters.AddWithValue("$hash", hash);
        update.Parameters.AddWithValue("$runId", runId);
        if (update.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("The editorial run is missing or is not in a preparable state.");
        }

        transaction.Commit();
        return new EditorialReportResult(runId, "ReadyToExport", markdown, hash, proposals.Count);
    }

    private static EditorialExportResult ConfirmExportCore(string databasePath, string runId, string reportPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("The exported editorial report does not exist.", reportPath);
        }

        var actualHash = Sha256(File.ReadAllText(reportPath));
        using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        EnsureEditorialSchema(connection, null);
        using var transaction = connection.BeginTransaction();
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT status, report_sha256, report_path FROM editorial_runs WHERE run_id = $runId";
        read.Parameters.AddWithValue("$runId", runId);
        using var reader = read.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("The editorial run does not exist.");
        }

        var status = reader.GetString(0);
        var expectedHash = reader.IsDBNull(1) ? null : reader.GetString(1);
        var previousPath = reader.IsDBNull(2) ? null : reader.GetString(2);
        reader.Close();
        if (expectedHash is null || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash)))
        {
            throw new InvalidOperationException("The exported editorial report does not match the prepared report fingerprint.");
        }

        if (string.Equals(status, "Exported", StringComparison.Ordinal) && !string.Equals(previousPath, reportPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The editorial run was already confirmed at another path.");
        }

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE editorial_runs SET status = 'Exported', report_path = $path, exported_at_utc = $now WHERE run_id = $runId";
        update.Parameters.AddWithValue("$path", reportPath);
        update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        update.Parameters.AddWithValue("$runId", runId);
        update.ExecuteNonQuery();
        transaction.Commit();
        return new EditorialExportResult(runId, "Exported", reportPath, actualHash);
    }

    private static CandidateDecision Decide(
        SelectionProposal proposal,
        IReadOnlyList<EditorialSource> sources,
        string eventFingerprint,
        float[] vector,
        IReadOnlyList<HistoricalEditorialProposal> history,
        IReadOnlyList<float[]> acceptedVectors,
        DateTimeOffset cutoff,
        string model,
        int dimension,
        double threshold,
        int incompatibleCount)
    {
        if (proposal.NoveltyDecision is EditorialNoveltyDecision.Duplicate or EditorialNoveltyDecision.Uncertain)
        {
            return CandidateDecision.Reject(proposal, sources, eventFingerprint, proposal.NoveltyDecision, "The structured selector did not establish novelty.");
        }

        var normalizedTitle = NormalizeTitle(proposal.Title);
        var exact = history.FirstOrDefault(item =>
            item.NormalizedTitle == normalizedTitle ||
            item.EventFingerprint == eventFingerprint ||
            item.SourceUrls.Overlaps(sources.Select(static source => source.CanonicalUrl)));
        if (exact is not null && !IsSupportedMaterialUpdate(proposal, exact))
        {
            return CandidateDecision.Reject(proposal, sources, eventFingerprint, EditorialNoveltyDecision.Duplicate, "An exact title, source, or event identity already exists.", exact.Id);
        }

        HistoricalEditorialProposal? nearest = null;
        var best = double.NegativeInfinity;
        foreach (var item in history.Where(item => item.CreatedAtUtc >= cutoff &&
                     string.Equals(item.EmbeddingModel, model, StringComparison.Ordinal) &&
                     item.EmbeddingDimension == dimension && item.Embedding.Length == dimension))
        {
            var score = Cosine(vector, item.Embedding);
            if (score > best)
            {
                best = score;
                nearest = item;
            }
        }

        var batchScore = acceptedVectors.Count == 0 ? double.NegativeInfinity : acceptedVectors.Max(item => Cosine(vector, item));
        if (batchScore >= threshold)
        {
            return CandidateDecision.Reject(proposal, sources, eventFingerprint, EditorialNoveltyDecision.Duplicate, "Another proposal in the current selection describes the same event.", similarity: batchScore);
        }

        if (nearest is not null && best >= threshold && !IsSupportedMaterialUpdate(proposal, nearest))
        {
            return CandidateDecision.Reject(proposal, sources, eventFingerprint, EditorialNoveltyDecision.Duplicate, "A semantically similar historical event already exists.", nearest.Id, best);
        }

        if (incompatibleCount > 0)
        {
            return CandidateDecision.Reject(proposal, sources, eventFingerprint, EditorialNoveltyDecision.Uncertain, "Semantic novelty coverage is incomplete because historical embeddings are incompatible.", nearest?.Id, double.IsFinite(best) ? best : null);
        }

        var finalDecision = exact is not null || nearest is not null && best >= threshold
            ? EditorialNoveltyDecision.MaterialUpdate
            : EditorialNoveltyDecision.New;
        return CandidateDecision.Accept(proposal, sources, eventFingerprint, finalDecision, finalDecision == EditorialNoveltyDecision.New ? "No duplicate event was found." : "A distinct version, date, and material fact support this update.", nearest?.Id, double.IsFinite(best) ? best : null);
    }

    private static bool IsSupportedMaterialUpdate(SelectionProposal proposal, HistoricalEditorialProposal history) =>
        proposal.NoveltyDecision == EditorialNoveltyDecision.MaterialUpdate &&
        proposal.ClaimedNewFacts.Count > 0 &&
        (!string.IsNullOrWhiteSpace(proposal.EventVersion) || proposal.EventDate is not null) &&
        (!string.Equals(proposal.EventVersion?.Trim(), history.EventVersion, StringComparison.OrdinalIgnoreCase) || proposal.EventDate != history.EventDate);

    private long InsertReservedProposal(SqliteConnection connection, SqliteTransaction transaction, CandidateDecision decision, string model, int dimension, float[] vector)
    {
        var primaryUrl = decision.Sources.FirstOrDefault()?.CanonicalUrl;
        var key = Sha256($"{NormalizeTitle(decision.Proposal.Title)}\u001f{primaryUrl}\u001f{decision.EventFingerprint}");
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO editorial_proposals
                (title, summary, source_url, created_at_utc, used, proposal_key, embedding_model, embedding_dimension, embedding)
            VALUES ($title, $summary, $url, $created, 0, $key, $model, $dimension, $embedding)
            ON CONFLICT(proposal_key) DO NOTHING;
            SELECT id FROM editorial_proposals WHERE proposal_key = $key;
            """;
        insert.Parameters.AddWithValue("$title", decision.Proposal.Title.Trim());
        insert.Parameters.AddWithValue("$summary", decision.Proposal.FactualSummary.Trim());
        insert.Parameters.AddWithValue("$url", (object?)primaryUrl ?? DBNull.Value);
        insert.Parameters.AddWithValue("$created", _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$key", key);
        insert.Parameters.AddWithValue("$model", model);
        insert.Parameters.AddWithValue("$dimension", dimension);
        insert.Parameters.Add("$embedding", SqliteType.Blob).Value = EncodeVector(vector);
        var id = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);

        using var metadata = connection.CreateCommand();
        metadata.Transaction = transaction;
        metadata.CommandText = "INSERT OR IGNORE INTO editorial_proposal_metadata (proposal_id, language, event_version, event_date, event_fingerprint, projection_version, entities_json) VALUES ($id, $language, $version, $date, $fingerprint, $projection, $entities)";
        metadata.Parameters.AddWithValue("$id", id);
        metadata.Parameters.AddWithValue("$language", decision.Proposal.Language?.Trim() ?? "und");
        metadata.Parameters.AddWithValue("$version", (object?)decision.Proposal.EventVersion?.Trim() ?? DBNull.Value);
        metadata.Parameters.AddWithValue("$date", decision.Proposal.EventDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        metadata.Parameters.AddWithValue("$fingerprint", decision.EventFingerprint);
        metadata.Parameters.AddWithValue("$projection", EditorialProjectionVersion);
        metadata.Parameters.AddWithValue("$entities", JsonSerializer.Serialize(decision.Proposal.Entities, EditorialJsonOptions));
        metadata.ExecuteNonQuery();

        foreach (var source in decision.Sources)
        {
            using var sourceInsert = connection.CreateCommand();
            sourceInsert.Transaction = transaction;
            sourceInsert.CommandText = "INSERT OR IGNORE INTO editorial_proposal_sources (proposal_id, source_id, canonical_url) VALUES ($id, $sourceId, $url)";
            sourceInsert.Parameters.AddWithValue("$id", id);
            sourceInsert.Parameters.AddWithValue("$sourceId", source.SourceId);
            sourceInsert.Parameters.AddWithValue("$url", source.CanonicalUrl);
            sourceInsert.ExecuteNonQuery();
        }

        foreach (var fact in decision.Proposal.ClaimedNewFacts)
        {
            var factFingerprint = Sha256(NormalizeTitle(fact.Text));
            using var factInsert = connection.CreateCommand();
            factInsert.Transaction = transaction;
            factInsert.CommandText = "INSERT OR IGNORE INTO editorial_proposal_facts (proposal_id, fact_fingerprint, fact_text) VALUES ($id, $fingerprint, $text)";
            factInsert.Parameters.AddWithValue("$id", id);
            factInsert.Parameters.AddWithValue("$fingerprint", factFingerprint);
            factInsert.Parameters.AddWithValue("$text", fact.Text.Trim());
            factInsert.ExecuteNonQuery();
            foreach (var sourceId in fact.SourceIds)
            {
                using var linkInsert = connection.CreateCommand();
                linkInsert.Transaction = transaction;
                linkInsert.CommandText = "INSERT OR IGNORE INTO editorial_fact_sources(proposal_id,fact_fingerprint,source_id) VALUES($id,$fingerprint,$sourceId)";
                linkInsert.Parameters.AddWithValue("$id", id);
                linkInsert.Parameters.AddWithValue("$fingerprint", factFingerprint);
                linkInsert.Parameters.AddWithValue("$sourceId", sourceId);
                linkInsert.ExecuteNonQuery();
            }
        }

        return id;
    }

    private static void EnsureEditorialSchema(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS editorial_schema_versions (version INTEGER PRIMARY KEY, applied_at_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS editorial_runs (
              run_id TEXT PRIMARY KEY, status TEXT NOT NULL CHECK (status IN ('Reserved','ReadyToExport','Exported','Failed')),
              created_at_utc TEXT NOT NULL, proposed_count INTEGER NOT NULL, approved_count INTEGER NOT NULL,
              rejected_count INTEGER NOT NULL, embedding_model TEXT NOT NULL, embedding_dimension INTEGER NOT NULL,
              projection_version TEXT NOT NULL, incompatible_embedding_count INTEGER NOT NULL,
              report_markdown TEXT NULL, report_sha256 TEXT NULL,
              report_path TEXT NULL, exported_at_utc TEXT NULL);
            CREATE TABLE IF NOT EXISTS editorial_proposal_metadata (
              proposal_id INTEGER PRIMARY KEY REFERENCES editorial_proposals(id), language TEXT NOT NULL,
              event_version TEXT NULL, event_date TEXT NULL, event_fingerprint TEXT NOT NULL,
              projection_version TEXT NOT NULL, entities_json TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_editorial_event_fingerprint ON editorial_proposal_metadata(event_fingerprint);
            CREATE TABLE IF NOT EXISTS editorial_proposal_sources (
              proposal_id INTEGER NOT NULL REFERENCES editorial_proposals(id), source_id TEXT NOT NULL,
              canonical_url TEXT NOT NULL, PRIMARY KEY (proposal_id, canonical_url));
            CREATE INDEX IF NOT EXISTS ix_editorial_source_url ON editorial_proposal_sources(canonical_url);
            CREATE TABLE IF NOT EXISTS editorial_proposal_facts (
              proposal_id INTEGER NOT NULL REFERENCES editorial_proposals(id), fact_fingerprint TEXT NOT NULL,
              fact_text TEXT NOT NULL, PRIMARY KEY (proposal_id, fact_fingerprint));
            CREATE TABLE IF NOT EXISTS editorial_fact_sources (
              proposal_id INTEGER NOT NULL, fact_fingerprint TEXT NOT NULL, source_id TEXT NOT NULL,
              PRIMARY KEY (proposal_id, fact_fingerprint, source_id),
              FOREIGN KEY (proposal_id, fact_fingerprint) REFERENCES editorial_proposal_facts(proposal_id, fact_fingerprint));
            CREATE TABLE IF NOT EXISTS editorial_run_proposals (
              run_id TEXT NOT NULL REFERENCES editorial_runs(run_id), temporary_id TEXT NOT NULL,
              proposal_id INTEGER NULL REFERENCES editorial_proposals(id), decision TEXT NOT NULL, reason TEXT NOT NULL,
              historical_proposal_id INTEGER NULL, similarity REAL NULL, PRIMARY KEY (run_id, temporary_id));
            CREATE TABLE IF NOT EXISTS editorial_import_provenance (
              proposal_id INTEGER NOT NULL REFERENCES editorial_proposals(id), report_path TEXT NOT NULL,
              historical_date TEXT NOT NULL, subject_ordinal INTEGER NOT NULL, content_sha256 TEXT NOT NULL,
              PRIMARY KEY (report_path, subject_ordinal));
            INSERT OR IGNORE INTO editorial_schema_versions(version, applied_at_utc) VALUES (2, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
            """;
        command.ExecuteNonQuery();
    }

    private static DiscoveryEnvelope ParseDiscovery(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var status = root.GetProperty("searchStatus").GetString() ?? string.Empty;
            var candidates = root.GetProperty("candidates").EnumerateArray().ToDictionary(
                static item => item.GetProperty("hitId").GetString() ?? string.Empty,
                static item => NormalizeUrl(item.GetProperty("canonicalUrl").GetString()) ?? throw new EditorialContractException("A discovered candidate has no valid canonical URL."),
                StringComparer.Ordinal);
            return new DiscoveryEnvelope(status, candidates);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or ArgumentException)
        {
            throw new EditorialContractException("The discovery output does not satisfy the editorial selection contract.", exception);
        }
    }

    private static SelectionEnvelope ParseSelection(string json) => Deserialize<SelectionEnvelope>(json, "selection");
    private static DraftEnvelope ParseDrafts(string json) => Deserialize<DraftEnvelope>(json, "draft");

    private static T Deserialize<T>(string json, string name) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, EditorialJsonOptions)
                ?? throw new EditorialContractException($"The editorial {name} is empty.");
        }
        catch (JsonException exception)
        {
            throw new EditorialContractException($"The editorial {name} JSON does not satisfy its contract.", exception);
        }
    }

    private static void ValidateSelection(SelectionEnvelope selection, DiscoveryEnvelope discovery)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var proposal in selection.Proposals)
        {
            if (string.IsNullOrWhiteSpace(proposal.TemporaryId) || !ids.Add(proposal.TemporaryId) ||
                string.IsNullOrWhiteSpace(proposal.Title) || string.IsNullOrWhiteSpace(proposal.FactualSummary) || proposal.SourceIds.Count == 0)
            {
                throw new EditorialContractException("Every selected proposal must have a unique temporary id, title, factual summary, and at least one source id.");
            }

            if (proposal.SourceIds.Distinct(StringComparer.Ordinal).Count() != proposal.SourceIds.Count || proposal.SourceIds.Any(id => !discovery.Candidates.ContainsKey(id)))
            {
                throw new EditorialContractException($"Proposal '{proposal.TemporaryId}' references an unknown or duplicate discovery source id.");
            }

            if (proposal.ClaimedNewFacts.Count == 0)
            {
                throw new EditorialContractException($"Proposal '{proposal.TemporaryId}' must identify at least one source-backed fact.");
            }

            if (proposal.ClaimedNewFacts.Any(fact => string.IsNullOrWhiteSpace(fact.Text) || fact.SourceIds.Count == 0 ||
                    fact.SourceIds.Any(id => !proposal.SourceIds.Contains(id, StringComparer.Ordinal))))
            {
                throw new EditorialContractException($"Proposal '{proposal.TemporaryId}' contains a fact without valid supporting source ids.");
            }
        }
    }

    private static string Projection(SelectionProposal proposal) => string.Join('\n',
        proposal.Title.Trim(), proposal.FactualSummary.Trim(),
        string.Join("; ", proposal.Entities.Select(static value => value.Trim()).Order(StringComparer.OrdinalIgnoreCase)),
        proposal.EventVersion?.Trim() ?? string.Empty,
        proposal.EventDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        string.Join("; ", proposal.ClaimedNewFacts.Select(static value => value.Text.Trim()).Order(StringComparer.OrdinalIgnoreCase)));

    private static string DraftProjection(DraftSubject draft) =>
        string.Join('\n', draft.WhyNow, draft.CriticalAngle, draft.Body, draft.LinkedInPost);

    private static string EventFingerprint(SelectionProposal proposal) => Sha256(string.Join('\n',
        proposal.Entities.Select(NormalizeTitle).Order(StringComparer.Ordinal),
        NormalizeTitle(proposal.EventVersion ?? string.Empty),
        proposal.EventDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        proposal.ClaimedNewFacts.Select(static fact => NormalizeTitle(fact.Text)).Order(StringComparer.Ordinal)));

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<FileStream> AcquireReservationLockAsync(string databasePath, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var gate = ProcessLocks.GetOrAdd(fullPath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lockPath = fullPath + ".editorial.lock";
            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return new ReleasingFileStream(lockPath, gate);
                }
                catch (IOException) when (attempt < 300)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    private static IReadOnlyList<HistoricalEditorialProposal> ReadEditorialHistory(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.title, p.created_at_utc, p.embedding_model, p.embedding_dimension, p.embedding,
                   m.event_version, m.event_date, m.event_fingerprint
            FROM editorial_proposals p LEFT JOIN editorial_proposal_metadata m ON m.proposal_id = p.id ORDER BY p.id
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<(long Id, string Title, DateTimeOffset Created, string Model, int Dimension, float[] Vector, string? Version, DateOnly? Date, string? Fingerprint)>();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            rows.Add((
                id,
                NormalizeTitle(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                reader.GetString(3),
                reader.GetInt32(4),
                DecodeVector((byte[])reader.GetValue(5), reader.GetInt32(4)),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : DateOnly.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }
        reader.Close();
        return rows.Select(row => new HistoricalEditorialProposal(
            row.Id, row.Title, row.Created, row.Model, row.Dimension, row.Vector,
            row.Version, row.Date, row.Fingerprint, ReadSourceUrls(connection, row.Id))).ToArray();
    }

    private static HashSet<string> ReadSourceUrls(SqliteConnection connection, long proposalId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT canonical_url FROM editorial_proposal_sources WHERE proposal_id = $id UNION SELECT source_url FROM editorial_proposals WHERE id = $id AND source_url IS NOT NULL";
        command.Parameters.AddWithValue("$id", proposalId);
        using var reader = command.ExecuteReader();
        var output = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) output.Add(NormalizeUrl(reader.GetString(0))!);
        return output;
    }

    private static void InsertRun(SqliteConnection connection, SqliteTransaction transaction, string runId, int proposed, int approved, int rejected, string model, int dimension, int incompatibleCount)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO editorial_runs(run_id,status,created_at_utc,proposed_count,approved_count,rejected_count,embedding_model,embedding_dimension,projection_version,incompatible_embedding_count) VALUES($id,'Reserved',$now,$proposed,$approved,$rejected,$model,$dimension,$projection,$incompatible)";
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$proposed", proposed);
        command.Parameters.AddWithValue("$approved", approved);
        command.Parameters.AddWithValue("$rejected", rejected);
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$dimension", dimension);
        command.Parameters.AddWithValue("$projection", EditorialProjectionVersion);
        command.Parameters.AddWithValue("$incompatible", incompatibleCount);
        command.ExecuteNonQuery();
    }

    private static void InsertRunDecision(SqliteConnection connection, SqliteTransaction transaction, string runId, string temporaryId, long? proposalId, EditorialNoveltyDecision decision, string reason, long? historicalId, double? similarity)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO editorial_run_proposals(run_id,temporary_id,proposal_id,decision,reason,historical_proposal_id,similarity) VALUES($run,$temporary,$proposal,$decision,$reason,$historical,$similarity)";
        command.Parameters.AddWithValue("$run", runId);
        command.Parameters.AddWithValue("$temporary", temporaryId);
        command.Parameters.AddWithValue("$proposal", (object?)proposalId ?? DBNull.Value);
        command.Parameters.AddWithValue("$decision", decision.ToString());
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$historical", (object?)historicalId ?? DBNull.Value);
        command.Parameters.AddWithValue("$similarity", (object?)similarity ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static EditorialReservationResult? ReadReservation(SqliteConnection connection, string runId)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT status,embedding_model,embedding_dimension,projection_version,incompatible_embedding_count FROM editorial_runs WHERE run_id = $id";
        exists.Parameters.AddWithValue("$id", runId);
        using var existingReader = exists.ExecuteReader();
        if (!existingReader.Read()) return null;
        var status = existingReader.GetString(0);
        var model = existingReader.GetString(1);
        var dimension = existingReader.GetInt32(2);
        var projection = existingReader.GetString(3);
        var incompatible = existingReader.GetInt32(4);
        existingReader.Close();
        var approved = ReadApprovedForReport(connection, runId).Select(item => new ReservedEditorialProposal(item.TemporaryId, item.ProposalId, item.Title, item.Summary, item.Sources, item.Decision)).ToArray();
        using var rejectedCommand = connection.CreateCommand();
        rejectedCommand.CommandText = "SELECT temporary_id,decision,reason,historical_proposal_id,similarity FROM editorial_run_proposals WHERE run_id=$id AND proposal_id IS NULL ORDER BY temporary_id";
        rejectedCommand.Parameters.AddWithValue("$id", runId);
        using var reader = rejectedCommand.ExecuteReader();
        var rejected = new List<RejectedEditorialProposal>();
        while (reader.Read()) rejected.Add(new(reader.GetString(0), Enum.Parse<EditorialNoveltyDecision>(reader.GetString(1)), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetDouble(4)));
        return new EditorialReservationResult(runId, status, approved, rejected, model, dimension, projection, incompatible);
    }

    private static EditorialReservationResult? ReadReservationFromDatabase(string databasePath, string runId, CancellationToken cancellationToken)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        ValidateSchema(connection, null);
        EnsureEditorialSchema(connection, null);
        return ReadReservation(connection, runId);
    }

    private static IReadOnlyList<ReportProposal> ReadApprovedForReport(SqliteConnection connection, string runId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rp.temporary_id,p.id,p.title,p.summary,rp.decision,p.embedding_model,p.embedding_dimension,p.embedding FROM editorial_run_proposals rp JOIN editorial_proposals p ON p.id=rp.proposal_id WHERE rp.run_id=$id ORDER BY rp.rowid";
        command.Parameters.AddWithValue("$id", runId);
        using var reader = command.ExecuteReader();
        var rows = new List<(string TemporaryId, long ProposalId, string Title, string Summary, EditorialNoveltyDecision Decision, string Model, int Dimension, float[] Embedding)>();
        while (reader.Read())
        {
            var dimension = reader.GetInt32(6);
            rows.Add((reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), Enum.Parse<EditorialNoveltyDecision>(reader.GetString(4)), reader.GetString(5), dimension, DecodeVector((byte[])reader.GetValue(7), dimension)));
        }
        reader.Close();
        return rows.Select(row => new ReportProposal(row.TemporaryId, row.ProposalId, row.Title, row.Summary, row.Decision, ReadSources(connection, row.ProposalId), row.Model, row.Embedding)).ToArray();
    }

    private static IReadOnlyList<EditorialSource> ReadSources(SqliteConnection connection, long proposalId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_id,canonical_url FROM editorial_proposal_sources WHERE proposal_id=$id ORDER BY rowid";
        command.Parameters.AddWithValue("$id", proposalId);
        using var reader = command.ExecuteReader();
        var output = new List<EditorialSource>();
        while (reader.Read()) output.Add(new(reader.GetString(0), reader.GetString(1)));
        return output;
    }

    private static string BuildMarkdown(string title, IReadOnlyList<ReportProposal> proposals, IReadOnlyDictionary<string, DraftSubject> drafts)
    {
        var builder = new StringBuilder().Append("# ").AppendLine(title).AppendLine();
        if (proposals.Count == 0)
        {
            return builder.AppendLine("Aucun sujet nouveau validé aujourd’hui.").ToString();
        }

        builder.AppendLine("## Synthèse").AppendLine().AppendLine("Les sujets ci-dessous ont été validés comme nouveaux ou comme évolutions matérielles, puis mémorisés avant export.").AppendLine();
        for (var index = 0; index < proposals.Count; index++)
        {
            var proposal = proposals[index];
            var draft = drafts[proposal.TemporaryId];
            builder.Append("## Sujet ").Append(index + 1).Append(" — ").AppendLine(proposal.Title).AppendLine()
                .AppendLine(proposal.Summary).AppendLine()
                .AppendLine("### Pourquoi maintenant ?").AppendLine().AppendLine(draft.WhyNow.Trim()).AppendLine()
                .AppendLine("### Angle critique").AppendLine().AppendLine(draft.CriticalAngle.Trim()).AppendLine()
                .AppendLine(draft.Body.Trim()).AppendLine().AppendLine("### Sources").AppendLine();
            foreach (var source in proposal.Sources) builder.Append("- ").AppendLine(source.CanonicalUrl);
            builder.AppendLine();
        }

        builder.AppendLine("## Propositions de posts LinkedIn").AppendLine();
        for (var index = 0; index < proposals.Count; index++)
        {
            builder.Append("### Post ").AppendLine((index + 1).ToString(CultureInfo.InvariantCulture)).AppendLine()
                .AppendLine(drafts[proposals[index].TemporaryId].LinkedInPost.Trim()).AppendLine();
        }
        return builder.ToString();
    }

    private sealed class ReleasingFileStream : FileStream
    {
        private readonly SemaphoreSlim _gate;
        public ReleasingFileStream(string path, SemaphoreSlim gate) : base(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous) => _gate = gate;
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);
            _gate.Release();
        }
    }

    private sealed record DiscoveryEnvelope(string SearchStatus, IReadOnlyDictionary<string, string> Candidates);
    private sealed record SelectionEnvelope(IReadOnlyList<SelectionProposal> Proposals);
    private sealed record SelectionProposal(string TemporaryId, string Title, string FactualSummary, IReadOnlyList<string> SourceIds, IReadOnlyList<string> Entities, string? EventVersion, DateOnly? EventDate, IReadOnlyList<ClaimedFact> ClaimedNewFacts, EditorialNoveltyDecision NoveltyDecision, string? Language);
    private sealed record ClaimedFact(string Text, IReadOnlyList<string> SourceIds);
    private sealed record DraftEnvelope(IReadOnlyList<DraftSubject> Subjects);
    private sealed record DraftSubject(string TemporaryId, string WhyNow, string CriticalAngle, string Body, string LinkedInPost);
    private sealed record HistoricalEditorialProposal(long Id, string NormalizedTitle, DateTimeOffset CreatedAtUtc, string EmbeddingModel, int EmbeddingDimension, float[] Embedding, string? EventVersion, DateOnly? EventDate, string? EventFingerprint, HashSet<string> SourceUrls);
    private sealed record ReportProposal(string TemporaryId, long ProposalId, string Title, string Summary, EditorialNoveltyDecision Decision, IReadOnlyList<EditorialSource> Sources, string EmbeddingModel, float[] Embedding);
    private sealed record CandidateDecision(SelectionProposal Proposal, IReadOnlyList<EditorialSource> Sources, string EventFingerprint, bool Approved, EditorialNoveltyDecision Decision, string Reason, long? HistoricalProposalId, double? Similarity)
    {
        public static CandidateDecision Accept(SelectionProposal proposal, IReadOnlyList<EditorialSource> sources, string fingerprint, EditorialNoveltyDecision decision, string reason, long? historicalId = null, double? similarity = null) => new(proposal, sources, fingerprint, true, decision, reason, historicalId, similarity);
        public static CandidateDecision Reject(SelectionProposal proposal, IReadOnlyList<EditorialSource> sources, string fingerprint, EditorialNoveltyDecision decision, string reason, long? historicalId = null, double? similarity = null) => new(proposal, sources, fingerprint, false, decision, reason, historicalId, similarity);
    }
}
