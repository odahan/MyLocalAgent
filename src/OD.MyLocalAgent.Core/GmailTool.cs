using System.ComponentModel;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes compact-JSON Gmail operations with summary-only protected reads.
/// </summary>
[ToolGroup(
    "gmail",
    "Gmail",
    "Read or send Gmail messages through the configured account.",
    100,
    Activation = ToolActivationKind.Singleton,
    ServiceContract = typeof(IGmailTool))]
public sealed class GmailTool : IGmailTool
{
    private const int AdaptiveReportOutputThresholdCharacters = 64_000;

    private const string ReviewMarker =
        "[GMAIL MESSAGE REQUIRES HUMAN REVIEW: compact source processing did not produce a deliverable protected summary. Review the original message in Gmail. Do not infer or act on omitted content.]";

    private readonly IGmailConnector _connector;
    private readonly IOptionsMonitor<GuardrailsOptions> _guardrailsOptions;
    private readonly IContentTrustService _contentTrustService;
    private readonly IIncidentReporter _incidentReporter;
    private readonly ILogger<GmailTool> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GmailTool"/> class.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public GmailTool(
        IGmailConnector connector,
        IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
        IContentTrustService contentTrustService,
        IIncidentReporter incidentReporter,
        ILogger<GmailTool> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(guardrailsOptions);
        ArgumentNullException.ThrowIfNull(contentTrustService);
        ArgumentNullException.ThrowIfNull(incidentReporter);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _connector = connector;
        _guardrailsOptions = guardrailsOptions;
        _contentTrustService = contentTrustService;
        _incidentReporter = incidentReporter;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    [Tool("gmail.readToday", "Reads Gmail messages received today.", 100)]
    public Task<string> ReadTodayAsync(
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional maximum number of messages to return.", Example = "10")] int? maxMessages = null,
        [ToolArgument(WorkflowArgumentKind.Boolean, "When true, automatically emits a lean report-oriented representation only if the complete Gmail result is too large for workflow context.", Example = "false")] bool adaptiveReportOutput = false,
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            "gmail.readToday",
            _connector.GetTodaysMessagesAsync,
            maxMessages,
            adaptiveReportOutput,
            cancellationToken);

    /// <inheritdoc />
    [Tool("gmail.readUnread", "Reads unread Gmail messages.", 200)]
    public Task<string> ReadUnreadAsync(
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional maximum number of messages to return.", Example = "10")] int? maxMessages = null,
        [ToolArgument(WorkflowArgumentKind.Boolean, "When true, automatically emits a lean report-oriented representation only if the complete Gmail result is too large for workflow context.", Example = "false")] bool adaptiveReportOutput = false,
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            "gmail.readUnread",
            _connector.GetUnreadMessagesAsync,
            maxMessages,
            adaptiveReportOutput,
            cancellationToken);

    /// <inheritdoc />
    [Tool("gmail.readRecent", "Reads Gmail messages since the start of the local day a number of days ago.", 300)]
    public Task<string> ReadRecentAsync(
        [ToolArgument(WorkflowArgumentKind.Integer, "Number of local calendar days before today to include; zero means today only.", Example = "3")] int daysBack,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional maximum number of messages to return.", Example = "10")] int? maxMessages = null,
        [ToolArgument(WorkflowArgumentKind.Boolean, "When true, automatically emits a lean report-oriented representation only if the complete Gmail result is too large for workflow context.", Example = "false")] bool adaptiveReportOutput = false,
        CancellationToken cancellationToken = default)
    {
        if (daysBack < 0 || daysBack > 3650)
        {
            throw new ArgumentOutOfRangeException(
                nameof(daysBack),
                daysBack,
                "Days back must be between 0 and 3650.");
        }

        return ReadAsync(
            "gmail.readRecent",
            (maximum, token) => _connector.GetRecentMessagesAsync(daysBack, maximum, token),
            maxMessages,
            adaptiveReportOutput,
            cancellationToken,
            new { daysBack, maxMessages, adaptiveReportOutput });
    }

    /// <inheritdoc />
    [Tool("gmail.send", "Sends a plain-text Gmail message when the task has Gmail send permission.", 400, RequiredCapability = TaskCapability.GmailSend)]
    public Task<string> SendAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Recipient email address.", Example = "someone@example.com", IsExcludedFromWorkflowTextEditor = true)] string to,
        [ToolArgument(WorkflowArgumentKind.String, "Message subject.", Example = "Daily report")] string subject,
        [ToolArgument(WorkflowArgumentKind.String, "Plain-text message body.", Example = "Here is today's summary.")] string body,
        CancellationToken cancellationToken)
    {
        if (!MailAddress.TryCreate(to, out _))
        {
            throw new ArgumentException("A valid recipient email address is required.", nameof(to));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(body);
        return ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "gmail.send",
            new
            {
                recipientCharacterCount = to.Length,
                subjectLength = subject.Length,
                bodyLength = body.Length,
            },
            async () => ToolJson.Serialize(
                await _connector.SendMessageAsync(to, subject, body, cancellationToken)
                    .ConfigureAwait(false)));
    }

    private Task<string> ReadAsync(
        string alias,
        Func<int?, CancellationToken, Task<IReadOnlyList<EmailMessageDto>>> read,
        int? maxMessages,
        bool adaptiveReportOutput,
        CancellationToken cancellationToken,
        object? logArguments = null)
    {
        if (maxMessages is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMessages),
                maxMessages,
                "Maximum messages must be greater than zero when supplied.");
        }

        return ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            alias,
            logArguments ?? new { maxMessages, adaptiveReportOutput },
            async () =>
            {
                var fetched = await read(maxMessages, cancellationToken).ConfigureAwait(false);
                var options = _guardrailsOptions.CurrentValue;
                var limit = options.MaxGmailMessagesPerOperation;
                var processedCount = limit == 0 ? fetched.Count : Math.Min(limit, fetched.Count);
                var messages = fetched.Take(processedCount).ToArray();
                var projections = messages.Select(
                    message => GmailContentProjector.Project(message, options.MaxContentChars)).ToArray();
                _logger.LogInformation(
                    "Gmail compact projection completed for {ItemCount} item(s).",
                    projections.Length);

                var protectedMessages = new List<ProtectedGmailMessage>(processedCount);
                if (processedCount > 0)
                {
                    var items = projections.Select(
                        (projection, index) => new CompactExternalContentItem(
                            $"item-{index + 1}",
                            projection.Content,
                            Math.Min(800, options.MaxSummaryChars))).ToArray();
                    LogBatchPlan(items, options.MaxContentChars);
                    var batchResult = await _contentTrustService
                        .ProcessCompactAsync(items, cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var failure in batchResult.Failures)
                    {
                        _ = await _incidentReporter.ReportAsync(
                                failure.Exception,
                                $"{alias} compact Guardrails {failure.Phase}",
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }

                    for (var index = 0; index < processedCount; index++)
                    {
                        protectedMessages.Add(CreateMessage(
                            messages[index],
                            projections[index],
                            batchResult.Items[index],
                            options));
                    }

                    _logger.LogInformation(
                        "Gmail compact Guardrails completed: fetched {FetchedCount}, projected {ProjectedCount}, screened {ScreenedCount}, summarized {SummarizedCount}, withheld {WithheldCount}, human review {ReviewCount}, original characters {OriginalCharacterCount}, projected characters {ProjectedCharacterCount}, omitted characters {OmittedCharacterCount}.",
                        fetched.Count,
                        projections.Length,
                        batchResult.Items.Count(static item => item.SourceTrust.Screened),
                        batchResult.Items.Count(static item => item.Status == CompactExternalContentStatus.Summarized),
                        batchResult.Items.Count(item =>
                            ContentTrustPolicy.Decide(
                                item.SummaryTrust ?? item.SourceTrust,
                                options) == TrustDisposition.Withhold),
                        batchResult.Items.Count(static item => item.RequiresHumanReview),
                        projections.Sum(static projection => projection.OriginalCanonicalCharacterCount),
                        projections.Sum(static projection => projection.ProjectedCharacterCount),
                        projections.Sum(static projection => projection.OmittedCharacterCount));
                }

                var skipped = fetched.Count - processedCount;
                var limitApplied = limit > 0 && skipped > 0;
                return SerializeReadResult(
                    protectedMessages,
                    fetched.Count,
                    processedCount,
                    maxMessages,
                    skipped,
                    limitApplied,
                    adaptiveReportOutput);
            },
            SummarizeReadResult);
    }

    private void LogBatchPlan(
        IReadOnlyList<CompactExternalContentItem> items,
        int maximumCharacters)
    {
        var batches = new List<int>();
        var count = 0;
        long characters = 0;
        foreach (var item in items)
        {
            if (count > 0 && (count == 12 || characters + item.Content.Length > 6L * maximumCharacters))
            {
                batches.Add(count);
                count = 0;
                characters = 0;
            }

            count++;
            characters += item.Content.Length;
        }

        batches.Add(count);
        for (var index = 0; index < batches.Count; index++)
        {
            _logger.LogInformation(
                "Gmail compact Guardrails batch {BatchIndex} of {BatchCount}: {ItemCount} item(s), source screening, summarization, and final screening as eligible.",
                index + 1,
                batches.Count,
                batches[index]);
        }
    }

    private static ProtectedGmailMessage CreateMessage(
        EmailMessageDto message,
        GmailContentProjection projection,
        CompactExternalContentResult result,
        GuardrailsOptions options)
    {
        var success = result.Status == CompactExternalContentStatus.Summarized &&
            result.Summary is not null && result.SummaryTrust is not null;
        var trust = success ? result.SummaryTrust! : result.SourceTrust;
        var block = success
            ? BuildDeliveredSummary(projection, result.Summary!)
            : BuildReviewBlock(projection);
        var delivery = GuardrailDelivery.Apply(block, trust, options);
        var withheld = ContentTrustPolicy.Decide(trust, options) == TrustDisposition.Withhold;
        return new ProtectedGmailMessage(
            message.Id,
            message.ReceivedAt,
            message.IsUnread,
            delivery.Content,
            delivery.ContentEncoding,
            trust.TrustScore,
            trust.Status.ToString(),
            trust.Screened,
            trust.SuspiciousIndicators.Count,
            trust.ProviderName,
            result.LatencyMs,
            result.Status.ToString(),
            !success || withheld,
            message.BodyPlain.Length,
            success,
            false,
            success
                ? "CompactCanonicalProjectionAndDeliveredSummary"
                : "CompactCanonicalProjectionSourceTrustAndProjectedHeadersReviewMarker",
            projection.OriginalCanonicalCharacterCount,
            projection.ProjectedCharacterCount,
            projection.OmittedCharacterCount,
            projection.ProjectionWasTruncated,
            projection.QuotedHistoryWasRemoved);
    }

    private static string BuildDeliveredSummary(
        GmailContentProjection projection,
        string summary) =>
        $"From: {projection.From}\nTo: {projection.To}\nSubject: {projection.Subject}\n" +
        $"Received: {projection.Received}\nAI-generated factual summary:\n{summary}";

    private static string BuildReviewBlock(GmailContentProjection projection) =>
        $"From: {projection.From}\nTo: {projection.To}\nSubject: {projection.Subject}\n" +
        $"Received: {projection.Received}\n{ReviewMarker}";

    private static string? SummarizeReadResult(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("totalFetched", out var fetched) ||
            !root.TryGetProperty("processed", out var processed))
        {
            return null;
        }

        return fetched.GetInt32() == processed.GetInt32()
            ? $"Returned {processed.GetInt32()} Gmail message(s)."
            : $"Fetched {fetched.GetInt32()} Gmail message(s); returned {processed.GetInt32()} after the Guardrails limit.";
    }

    private static string SerializeReadResult(
        IReadOnlyList<ProtectedGmailMessage> messages,
        int totalFetched,
        int processed,
        int? requestedMaxMessages,
        int skippedBecauseOfLimit,
        bool limitApplied,
        bool adaptiveReportOutput)
    {
        var warning = limitApplied
            ? $"Guardrails processed only the first {processed} of {totalFetched} Gmail messages because the user-configured limit is active."
            : null;
        var detailed = ToolJson.Serialize(
            new
            {
                messages,
                totalFetched,
                processed,
                requestedMaxMessages,
                skippedBecauseOfLimit,
                limitApplied,
                warning,
                outputFormat = "detailed",
            });
        if (!adaptiveReportOutput ||
            detailed.Length <= AdaptiveReportOutputThresholdCharacters)
        {
            return detailed;
        }

        return ToolJson.Serialize(
            new
            {
                entries = messages.Select(static message => new
                {
                    message.IsUnread,
                    message.Content,
                    message.ContentEncoding,
                    message.RequiresHumanReview,
                }),
                totalFetched,
                processed,
                requestedMaxMessages,
                skippedBecauseOfLimit,
                limitApplied,
                warning,
                outputFormat = "compact-report",
                compactedBecauseDetailedOutputCharacters = detailed.Length,
            });
    }

    private sealed record ProtectedGmailMessage(
        string Id,
        DateTimeOffset ReceivedAt,
        bool IsUnread,
        string Content,
        string ContentEncoding,
        int? TrustScore,
        string TrustStatus,
        bool Screened,
        int SuspiciousIndicatorCount,
        string ProviderName,
        long ScreeningLatencyMs,
        string BodyProcessingStatus,
        bool RequiresHumanReview,
        int OriginalBodyCharacterCount,
        bool ContentWasSummarized,
        bool SummaryWasTruncated,
        string TrustScope,
        int OriginalCanonicalCharacterCount,
        int ProjectedCharacterCount,
        int OmittedCharacterCount,
        bool ProjectionWasTruncated,
        bool QuotedHistoryWasRemoved);
}
