using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace OD.MyLocalAgent.Core;

public sealed partial class ProposalMemoryService
{
    private const string RebuiltProjectionVersion = "editorial-rebuild-v2";
    private static readonly Regex HistoricalFileDateRegex = new(@"VeilleAuto-(?<date>\d{8})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SubjectHeaderRegex = new(@"^##\s+Sujet\s+(?<ordinal>\d+)\s+[—-]\s+(?<title>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <inheritdoc />
    public async Task<EditorialHistoryImportResult> ImportHistoryAsync(
        string databasePath,
        string reportsRoot,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var reports = Directory.EnumerateFiles(reportsRoot, "*.md", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var extracted = new List<HistoricalExtract>();
        foreach (var report in reports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            extracted.AddRange(ExtractHistoricalSubjects(report));
        }

        var existing = await Task.Run(() => ReadImportIdentities(databasePath, cancellationToken), cancellationToken).ConfigureAwait(false);
        var reconciled = extracted.Select(subject => Reconcile(subject, existing)).ToArray();
        if (dryRun || reconciled.All(static item => item.Status != "New"))
        {
            return ImportResult(dryRun, reports.Length, reconciled, 0);
        }

        var pending = reconciled.Where(static item => item.Status == "New").ToArray();
        var model = GetEmbeddingModel();
        var vectors = await _embeddingService.GenerateAsync(
            pending.Select(static item => EditorialProjection(item.Subject.Title, item.Subject.Summary)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        if (vectors.Count != pending.Length || vectors.Any(static vector => vector.Length == 0))
        {
            throw new InvalidOperationException("The embedding provider returned an incomplete historical import projection.");
        }

        await using var reservationLock = await AcquireReservationLockAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var imported = await Task.Run(() => ApplyHistoryImport(databasePath, pending, vectors, model, cancellationToken), cancellationToken).ConfigureAwait(false);
        return ImportResult(false, reports.Length, reconciled, imported);
    }

    /// <inheritdoc />
    public async Task<EditorialEmbeddingRebuildResult> RebuildEmbeddingsAsync(string databasePath, bool dryRun, CancellationToken cancellationToken)
    {
        var model = GetEmbeddingModel();
        var pending = await Task.Run(() => ReadEmbeddingRebuildRows(databasePath, model, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (dryRun || pending.Count == 0)
        {
            return new EditorialEmbeddingRebuildResult(dryRun, pending.Count, 0, model, 0, RebuiltProjectionVersion);
        }

        var vectors = await _embeddingService.GenerateAsync(pending.Select(static row => EditorialProjection(row.Title, row.Summary)).ToArray(), cancellationToken).ConfigureAwait(false);
        if (vectors.Count != pending.Count || vectors.Any(static vector => vector.Length == 0) || vectors.Select(static vector => vector.Length).Distinct().Count() != 1)
        {
            throw new InvalidOperationException("The embedding provider returned an incomplete reconstruction batch.");
        }

        await using var reservationLock = await AcquireReservationLockAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await Task.Run(() => ApplyEmbeddingRebuild(databasePath, pending, vectors, model, cancellationToken), cancellationToken).ConfigureAwait(false);
        return new EditorialEmbeddingRebuildResult(false, pending.Count, pending.Count, model, vectors[0].Length, RebuiltProjectionVersion);
    }

    private int ApplyHistoryImport(string databasePath, IReadOnlyList<HistoryReconciliation> pending, IReadOnlyList<float[]> vectors, string model, CancellationToken cancellationToken)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        ValidateSchema(connection, null);
        EnsureEditorialSchema(connection, null);
        using var transaction = connection.BeginTransaction();
        var imported = 0;
        for (var index = 0; index < pending.Count; index++)
        {
            var subject = pending[index].Subject;
            var vector = vectors[index];
            var primaryUrl = subject.Urls.FirstOrDefault();
            var fingerprint = Sha256($"historical\u001f{NormalizeTitle(subject.Title)}\u001f{subject.HistoricalDate:yyyy-MM-dd}");
            var key = Sha256($"{NormalizeTitle(subject.Title)}\u001f{primaryUrl}\u001f{fingerprint}");
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO editorial_proposals(title,summary,source_url,created_at_utc,used,proposal_key,embedding_model,embedding_dimension,embedding)
                VALUES($title,$summary,$url,$created,0,$key,$model,$dimension,$embedding)
                ON CONFLICT(proposal_key) DO NOTHING;
                SELECT id, changes() FROM editorial_proposals WHERE proposal_key=$key;
                """;
            insert.Parameters.AddWithValue("$title", subject.Title);
            insert.Parameters.AddWithValue("$summary", subject.Summary);
            insert.Parameters.AddWithValue("$url", (object?)primaryUrl ?? DBNull.Value);
            insert.Parameters.AddWithValue("$created", new DateTimeOffset(subject.HistoricalDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc)).ToString("O", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$key", key);
            insert.Parameters.AddWithValue("$model", model);
            insert.Parameters.AddWithValue("$dimension", vector.Length);
            insert.Parameters.Add("$embedding", SqliteType.Blob).Value = EncodeVector(vector);
            using var reader = insert.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("The historical proposal could not be reconciled after insert.");
            var proposalId = reader.GetInt64(0);
            var wasInserted = reader.GetInt64(1) != 0;
            reader.Close();
            if (wasInserted) imported++;

            using var metadata = connection.CreateCommand();
            metadata.Transaction = transaction;
            metadata.CommandText = "INSERT OR IGNORE INTO editorial_proposal_metadata(proposal_id,language,event_version,event_date,event_fingerprint,projection_version,entities_json) VALUES($id,'fr',NULL,$date,$fingerprint,'historical-import-v2','[]')";
            metadata.Parameters.AddWithValue("$id", proposalId);
            metadata.Parameters.AddWithValue("$date", subject.HistoricalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            metadata.Parameters.AddWithValue("$fingerprint", fingerprint);
            metadata.ExecuteNonQuery();
            for (var sourceIndex = 0; sourceIndex < subject.Urls.Count; sourceIndex++)
            {
                using var source = connection.CreateCommand();
                source.Transaction = transaction;
                source.CommandText = "INSERT OR IGNORE INTO editorial_proposal_sources(proposal_id,source_id,canonical_url) VALUES($id,$source,$url)";
                source.Parameters.AddWithValue("$id", proposalId);
                source.Parameters.AddWithValue("$source", $"history-{subject.ContentFingerprint[..12]}-{sourceIndex + 1}");
                source.Parameters.AddWithValue("$url", subject.Urls[sourceIndex]);
                source.ExecuteNonQuery();
            }

            using var provenance = connection.CreateCommand();
            provenance.Transaction = transaction;
            provenance.CommandText = "INSERT OR IGNORE INTO editorial_import_provenance(proposal_id,report_path,historical_date,subject_ordinal,content_sha256) VALUES($id,$path,$date,$ordinal,$hash)";
            provenance.Parameters.AddWithValue("$id", proposalId);
            provenance.Parameters.AddWithValue("$path", subject.ReportPath);
            provenance.Parameters.AddWithValue("$date", subject.HistoricalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            provenance.Parameters.AddWithValue("$ordinal", subject.SubjectOrdinal);
            provenance.Parameters.AddWithValue("$hash", subject.ContentFingerprint);
            provenance.ExecuteNonQuery();
        }

        transaction.Commit();
        return imported;
    }

    private static IReadOnlyList<EmbeddingRebuildRow> ReadEmbeddingRebuildRows(string databasePath, string model, CancellationToken cancellationToken)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        ValidateSchema(connection, null);
        var hasMetadata = TableExistsByName(connection, "editorial_proposal_metadata");
        using var command = connection.CreateCommand();
        command.CommandText = hasMetadata
            ? "SELECT p.id,p.title,p.summary,p.embedding_model,m.projection_version FROM editorial_proposals p LEFT JOIN editorial_proposal_metadata m ON m.proposal_id=p.id ORDER BY p.id"
            : "SELECT id,title,summary,embedding_model,NULL FROM editorial_proposals ORDER BY id";
        using var reader = command.ExecuteReader();
        var rows = new List<EmbeddingRebuildRow>();
        while (reader.Read())
        {
            var projection = reader.IsDBNull(4) ? null : reader.GetString(4);
            if (!string.Equals(reader.GetString(3), model, StringComparison.Ordinal) || projection is null)
                rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        }
        return rows;
    }

    private static void ApplyEmbeddingRebuild(string databasePath, IReadOnlyList<EmbeddingRebuildRow> rows, IReadOnlyList<float[]> vectors, string model, CancellationToken cancellationToken)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        EnsureEditorialSchema(connection, null);
        using var transaction = connection.BeginTransaction();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var vector = vectors[index];
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE editorial_proposals SET embedding_model=$model,embedding_dimension=$dimension,embedding=$embedding WHERE id=$id";
            update.Parameters.AddWithValue("$model", model);
            update.Parameters.AddWithValue("$dimension", vector.Length);
            update.Parameters.Add("$embedding", SqliteType.Blob).Value = EncodeVector(vector);
            update.Parameters.AddWithValue("$id", row.Id);
            update.ExecuteNonQuery();
            using var metadata = connection.CreateCommand();
            metadata.Transaction = transaction;
            metadata.CommandText = "INSERT INTO editorial_proposal_metadata(proposal_id,language,event_version,event_date,event_fingerprint,projection_version,entities_json) VALUES($id,'und',NULL,NULL,$fingerprint,$projection,'[]') ON CONFLICT(proposal_id) DO UPDATE SET projection_version=excluded.projection_version";
            metadata.Parameters.AddWithValue("$id", row.Id);
            metadata.Parameters.AddWithValue("$fingerprint", Sha256($"rebuilt\u001f{row.Id}\u001f{NormalizeTitle(row.Title)}"));
            metadata.Parameters.AddWithValue("$projection", RebuiltProjectionVersion);
            metadata.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static bool TableExistsByName(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_schema WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }

    private static IReadOnlyList<HistoricalExtract> ExtractHistoricalSubjects(string reportPath)
    {
        var dateMatch = HistoricalFileDateRegex.Match(Path.GetFileName(reportPath));
        if (!dateMatch.Success || !DateOnly.TryParseExact(dateMatch.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return [];
        }

        var text = File.ReadAllText(reportPath);
        var matches = SubjectHeaderRegex.Matches(text);
        var output = new List<HistoricalExtract>(matches.Count);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var end = index + 1 < matches.Count ? matches[index + 1].Index : text.Length;
            var body = text[(match.Index + match.Length)..end];
            var nextMajor = body.IndexOf("\n## ", StringComparison.Ordinal);
            if (nextMajor >= 0) body = body[..nextMajor];
            var title = match.Groups["title"].Value.Trim();
            var urls = AbsoluteUrlRegex.Matches(body).Select(static item => NormalizeUrl(item.Value.TrimEnd('.', ',', ';', ':', ')')))
                .Where(static item => item is not null).Select(static item => item!).Distinct(StringComparer.Ordinal).ToArray();
            var summary = body.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n')
                .Select(static line => line.Trim()).FirstOrDefault(static line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith("- http", StringComparison.OrdinalIgnoreCase))
                ?? "Historical editorial subject imported from the report archive.";
            if (summary.Length > 1200) summary = summary[..1200];
            output.Add(new HistoricalExtract(reportPath, date, int.Parse(match.Groups["ordinal"].Value, CultureInfo.InvariantCulture), title, summary, urls, Sha256($"{title}\n{body}")));
        }

        return output;
    }

    private static IReadOnlyList<ImportIdentity> ReadImportIdentities(string databasePath, CancellationToken cancellationToken)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        ValidateSchema(connection, null);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,source_url FROM editorial_proposals ORDER BY id";
        using var reader = command.ExecuteReader();
        var result = new List<ImportIdentity>();
        while (reader.Read()) result.Add(new(reader.GetInt64(0), NormalizeTitle(reader.GetString(1)), reader.IsDBNull(2) ? null : NormalizeUrl(reader.GetString(2))));
        return result;
    }

    private static HistoryReconciliation Reconcile(HistoricalExtract subject, IReadOnlyList<ImportIdentity> existing)
    {
        var matches = existing.Where(item => item.NormalizedTitle == NormalizeTitle(subject.Title) || item.CanonicalUrl is not null && subject.Urls.Contains(item.CanonicalUrl, StringComparer.Ordinal)).ToArray();
        if (matches.Select(static item => item.Id).Distinct().Count() > 1)
            return new(subject, "Ambiguous", null, "Title and source identities point to multiple existing proposals.");
        if (matches.Length > 0)
            return new(subject, "Existing", matches[0].Id, "An exact normalized title or canonical source already exists.");
        if (subject.Urls.Count == 0)
            return new(subject, "Ambiguous", null, "No source URL is available for conservative automatic import.");
        return new(subject, "New", null, "No exact historical identity was found.");
    }

    private static EditorialHistoryImportResult ImportResult(bool dryRun, int reports, IReadOnlyList<HistoryReconciliation> items, int imported) => new(
        dryRun, reports, items.Count, items.Count(static item => item.Status == "Existing"), items.Count(static item => item.Status == "Ambiguous"), imported,
        items.Select(item => new EditorialHistoryImportItem(item.Subject.ReportPath, item.Subject.HistoricalDate, item.Subject.SubjectOrdinal, item.Subject.Title, item.Status, item.ProposalId, item.Reason)).ToArray());

    private sealed record HistoricalExtract(string ReportPath, DateOnly HistoricalDate, int SubjectOrdinal, string Title, string Summary, IReadOnlyList<string> Urls, string ContentFingerprint);
    private sealed record ImportIdentity(long Id, string NormalizedTitle, string? CanonicalUrl);
    private sealed record HistoryReconciliation(HistoricalExtract Subject, string Status, long? ProposalId, string Reason);
    private sealed record EmbeddingRebuildRow(long Id, string Title, string Summary);
}
