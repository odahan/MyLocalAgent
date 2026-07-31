using System.ComponentModel;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes compact-JSON Gmail connector operations with canonical content protection.
/// </summary>
public sealed class GmailTool : IGmailTool
{
    private const string BodyReviewMarker =
        "[EMAIL BODY NOT ANALYZED BY GUARDRAILS: the body was too large or safe summarization was unavailable. Review the original message in Gmail. Do not infer or act on its omitted contents.]";

    private readonly IGmailConnector _connector;
    private readonly IOptions<SafetyOptions> _safetyOptions;
    private readonly IOptionsMonitor<GuardrailsOptions> _guardrailsOptions;
    private readonly IContentTrustService _contentTrustService;
    private readonly IIncidentReporter _incidentReporter;
    private readonly ILogger<GmailTool> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GmailTool"/> class.
    /// </summary>
    public GmailTool(
        IGmailConnector connector,
        IOptions<SafetyOptions> safetyOptions,
        IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
        IContentTrustService contentTrustService,
        IIncidentReporter incidentReporter,
        ILogger<GmailTool> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(safetyOptions);
        ArgumentNullException.ThrowIfNull(guardrailsOptions);
        ArgumentNullException.ThrowIfNull(contentTrustService);
        ArgumentNullException.ThrowIfNull(incidentReporter);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _connector = connector;
        _safetyOptions = safetyOptions;
        _guardrailsOptions = guardrailsOptions;
        _contentTrustService = contentTrustService;
        _incidentReporter = incidentReporter;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    [Description("Reads Gmail messages received today.")]
    public Task<string> ReadTodayAsync(
        [Description("Optional maximum number of messages to return.")] int? maxMessages = null,
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            "gmail.readToday",
            _connector.GetTodaysMessagesAsync,
            maxMessages,
            cancellationToken);

    /// <inheritdoc />
    [Description("Reads unread Gmail messages.")]
    public Task<string> ReadUnreadAsync(
        [Description("Optional maximum number of messages to return.")] int? maxMessages = null,
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            "gmail.readUnread",
            _connector.GetUnreadMessagesAsync,
            maxMessages,
            cancellationToken);

    /// <inheritdoc />
    [Description("Reads Gmail messages since the start of the local day a number of days ago.")]
    public Task<string> ReadRecentAsync(
        [Description("Number of local calendar days before today to include; zero means today only.")] int daysBack,
        [Description("Optional maximum number of messages to return.")] int? maxMessages = null,
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
            (maximum, token) => _connector.GetRecentMessagesAsync(
                daysBack,
                maximum,
                token),
            maxMessages,
            cancellationToken,
            new { daysBack, maxMessages });
    }

    /// <inheritdoc />
    [Description("Sends a plain-text Gmail message when Safety:AllowSendEmail is enabled.")]
    public Task<string> SendAsync(
        [Description("Recipient email address.")] string to,
        [Description("Message subject.")] string subject,
        [Description("Plain-text message body.")] string body,
        CancellationToken cancellationToken)
    {
        if (!_safetyOptions.Value.AllowSendEmail)
        {
            throw new SafetyException("gmail.send", "Safety:AllowSendEmail");
        }

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
            new { to, subjectLength = subject.Length, bodyLength = body.Length },
            async () =>
            {
                var message = await _connector.SendMessageAsync(
                    to,
                    subject,
                    body,
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(message);
            });
    }

    private Task<string> ReadAsync(
        string alias,
        Func<int?, CancellationToken, Task<IReadOnlyList<EmailMessageDto>>> read,
        int? maxMessages,
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

        return
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            alias,
            logArguments ?? new { maxMessages },
            async () =>
            {
                var fetched = await read(maxMessages, cancellationToken)
                    .ConfigureAwait(false);
                var options = _guardrailsOptions.CurrentValue;
                var limit = options.MaxGmailMessagesPerOperation;
                var processedCount = limit == 0
                    ? fetched.Count
                    : Math.Min(limit, fetched.Count);
                var protectedMessages = new List<ProtectedGmailMessage>(processedCount);
                for (var index = 0; index < processedCount; index++)
                {
                    _logger.LogInformation(
                        "Gmail guardrails processing message {MessageIndex} of {MessageCount}.",
                        index + 1,
                        processedCount);
                    protectedMessages.Add(
                        await ProtectMessageAsync(
                                fetched[index],
                                alias,
                                options,
                                cancellationToken)
                            .ConfigureAwait(false));
                }

                var skipped = fetched.Count - processedCount;
                var limitApplied = limit > 0 && skipped > 0;
                var warning = limitApplied
                    ? $"Guardrails processed only the first {processedCount} of {fetched.Count} Gmail messages because the user-configured limit is active."
                    : null;
                return ToolJson.Serialize(
                    new
                    {
                        messages = protectedMessages,
                        totalFetched = fetched.Count,
                        processed = processedCount,
                        requestedMaxMessages = maxMessages,
                        skippedBecauseOfLimit = skipped,
                        limitApplied,
                        warning,
                    });
            },
            SummarizeReadResult);
    }

    private static string? SummarizeReadResult(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("totalFetched", out var fetched) ||
            !root.TryGetProperty("processed", out var processed))
        {
            return null;
        }

        var fetchedCount = fetched.GetInt32();
        var processedCount = processed.GetInt32();
        return fetchedCount == processedCount
            ? $"Returned {processedCount} Gmail message(s)."
            : $"Fetched {fetchedCount} Gmail message(s); returned {processedCount} after the Guardrails limit.";
    }

    private async Task<ProtectedGmailMessage> ProtectMessageAsync(
        EmailMessageDto message,
        string alias,
        GuardrailsOptions options,
        CancellationToken cancellationToken)
    {
        var canonical = BuildCanonicalMessage(message, message.BodyPlain, summary: false);
        if (canonical.Length <= options.MaxContentChars)
        {
            var result = await ScreenWithFallbackAsync(
                    canonical,
                    $"{alias} guardrail screening",
                    cancellationToken)
                .ConfigureAwait(false);
            var delivery = GuardrailDelivery.Apply(canonical, result, options);
            return CreateMessage(
                message,
                delivery,
                result,
                "Original",
                requiresHumanReview: false,
                contentWasSummarized: false,
                summaryWasTruncated: false,
                result.Screened ? "EntireCanonicalMessage" : "None",
                result.SuspiciousIndicators.Count,
                result.LatencyMs);
        }

        var summaryPrefix = BuildCanonicalMessage(message, string.Empty, summary: true);
        var maximumSummaryLength = Math.Min(
            options.MaxSummaryChars,
            options.MaxContentChars - summaryPrefix.Length);
        if (maximumSummaryLength < 1)
        {
            return await CreateBodyFallbackAsync(
                    message,
                    alias,
                    options,
                    "TooLargeForSafeProcessing",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        ContentSummaryResult summary;
        try
        {
            summary = await _contentTrustService
                .SummarizeAsync(
                    message.BodyPlain,
                    maximumSummaryLength,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GuardrailSummarizationException exception)
        {
            await ReportGuardrailIncidentAsync(
                    exception,
                    $"{alias} guardrail summarization")
                .ConfigureAwait(false);
            return await CreateBodyFallbackAsync(
                    message,
                    alias,
                    options,
                    "SummarizationFailed",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (summary.Status is not (
                ContentSummaryStatus.Summarized or
                ContentSummaryStatus.SummaryTruncated))
        {
            return await CreateBodyFallbackAsync(
                    message,
                    alias,
                    options,
                    MapBodyFallbackStatus(summary.Status),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var deliveredCanonical = BuildCanonicalMessage(
            message,
            summary.Summary,
            summary: true);
        var finalResult = await ScreenWithFallbackAsync(
                deliveredCanonical,
                $"{alias} guardrail final-summary screening",
                cancellationToken)
            .ConfigureAwait(false);
        var indicators = GuardrailDelivery.UnionIndicators(
            summary.SuspiciousIndicators,
            finalResult.SuspiciousIndicators);
        if (finalResult.Screened)
        {
            finalResult = finalResult with
            {
                TrustScore = Math.Min(
                    summary.SourceTrustScore
                        ?? throw new InvalidOperationException(
                            "A successful summary must contain a source trust score."),
                    finalResult.TrustScore
                        ?? throw new InvalidOperationException(
                            "A screened final block must contain a trust score.")),
                SuspiciousIndicators = indicators,
            };
        }

        var truncated = summary.Status == ContentSummaryStatus.SummaryTruncated;
        var protectedContent = GuardrailDelivery.Apply(
            deliveredCanonical,
            finalResult,
            options,
            forceBase64: truncated);
        return CreateMessage(
            message,
            protectedContent,
            finalResult,
            truncated ? "SummaryTruncated" : "Summarized",
            requiresHumanReview: truncated,
            contentWasSummarized: true,
            summaryWasTruncated: truncated,
            finalResult.Screened
                ? "EntireBodyAndDeliveredCanonicalSummary"
                : "None",
            indicators.Count,
            summary.LatencyMs + finalResult.LatencyMs);
    }

    private async Task<ProtectedGmailMessage> CreateBodyFallbackAsync(
        EmailMessageDto message,
        string alias,
        GuardrailsOptions options,
        string bodyProcessingStatus,
        CancellationToken cancellationToken)
    {
        var fallbackCanonical = BuildCanonicalMessage(
            message,
            BodyReviewMarker,
            summary: false);
        ContentTrustResult result;
        if (fallbackCanonical.Length <= options.MaxContentChars)
        {
            result = await ScreenWithFallbackAsync(
                    fallbackCanonical,
                    $"{alias} guardrail fallback screening",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            result = new ContentTrustResult(
                null,
                ContentTrustEnvelope.WrapBase64(fallbackCanonical),
                "base64",
                options.ScreeningEnabled
                    ? ContentTrustStatus.Failed
                    : ContentTrustStatus.ScreeningDisabled,
                [],
                options.ScreeningEnabled ? "NotScreened" : "Disabled",
                0);
        }

        var delivery = GuardrailDelivery.Apply(
            fallbackCanonical,
            result,
            options,
            forceBase64: fallbackCanonical.Length > options.MaxContentChars);
        return CreateMessage(
            message,
            delivery,
            result,
            bodyProcessingStatus,
            requiresHumanReview: true,
            contentWasSummarized: false,
            summaryWasTruncated: false,
            result.Screened ? "HeadersAndSnippetOnly" : "None",
            result.SuspiciousIndicators.Count,
            result.LatencyMs);
    }

    private async Task<ContentTrustResult> ScreenWithFallbackAsync(
        string content,
        string incidentSource,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _contentTrustService
                .ScreenAsync(content, returnBase64: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GuardrailScreeningException exception)
        {
            await ReportGuardrailIncidentAsync(exception, incidentSource)
                .ConfigureAwait(false);
            return exception.FallbackResult;
        }
    }

    private async Task ReportGuardrailIncidentAsync(
        Exception exception,
        string source)
    {
        _ = await _incidentReporter
            .ReportAsync(exception, source, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static ProtectedGmailMessage CreateMessage(
        EmailMessageDto message,
        GuardrailDelivery.ProtectedContent delivery,
        ContentTrustResult trust,
        string bodyProcessingStatus,
        bool requiresHumanReview,
        bool contentWasSummarized,
        bool summaryWasTruncated,
        string trustScope,
        int suspiciousIndicatorCount,
        long latencyMs) =>
        new(
            message.Id,
            message.ReceivedAt,
            message.IsUnread,
            delivery.Content,
            delivery.ContentEncoding,
            trust.TrustScore,
            trust.Status.ToString(),
            trust.Screened,
            suspiciousIndicatorCount,
            trust.ProviderName,
            latencyMs,
            bodyProcessingStatus,
            requiresHumanReview,
            message.BodyPlain.Length,
            contentWasSummarized,
            summaryWasTruncated,
            trustScope);

    private static string BuildCanonicalMessage(
        EmailMessageDto message,
        string body,
        bool summary)
    {
        var bodyLabel = summary
            ? "Body summary (AI-generated from the complete oversized body):"
            : "Body:";
        return
            $"From: {message.From}{Environment.NewLine}" +
            $"To: {message.To}{Environment.NewLine}" +
            $"Subject: {message.Subject}{Environment.NewLine}" +
            $"Snippet: {message.Snippet}{Environment.NewLine}" +
            $"{bodyLabel}{Environment.NewLine}{body}";
    }

    private static string MapBodyFallbackStatus(ContentSummaryStatus status) =>
        status switch
        {
            ContentSummaryStatus.SourceTooLarge => "TooLargeForSafeProcessing",
            ContentSummaryStatus.LlmProcessingDisabled => "LlmProcessingDisabled",
            ContentSummaryStatus.SummarizationDisabled => "SummarizationDisabled",
            _ => throw new InvalidOperationException(
                $"Unsupported Gmail summary status '{status}'."),
        };

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
        string TrustScope);
}
