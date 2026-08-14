using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Provides human-facing prompt-injection trust screening and reduction diagnostics.
/// </summary>
public sealed partial class TrustFilterViewModel : ObservableObject
{
    private readonly IContentTrustService _contentTrustService;
    private readonly IOptionsMonitor<GuardrailsOptions> _guardrailsOptions;
    private readonly IIncidentReporter _incidentReporter;
    private bool _lastScreeningFailureWasTimeout;

    [ObservableProperty]
    private string _input = string.Empty;

    [ObservableProperty]
    private bool _returnBase64;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _trustStatus = "Not screened";

    [ObservableProperty]
    private string _contentProcessingStatus = "Original";

    [ObservableProperty]
    private int? _trustScore;

    [ObservableProperty]
    private string _scoreBadgeState = "Unknown";

    [ObservableProperty]
    private string _resultContent =
        "Enter external content and select Screen.";

    [ObservableProperty]
    private string _contentEncoding = "none";

    [ObservableProperty]
    private string _providerAndLatency = "Provider and latency are not available.";

    [ObservableProperty]
    private string _lengthDetails = string.Empty;

    [ObservableProperty]
    private string _explanation =
        "This probabilistic filter reduces risk but is not a security boundary.";

    /// <summary>
    /// Initializes a new instance of the <see cref="TrustFilterViewModel"/> class.
    /// </summary>
    /// <param name="contentTrustService">The content trust service.</param>
    /// <param name="guardrailsOptions">The reloadable guardrail settings.</param>
    /// <param name="incidentReporter">The application incident reporter.</param>
    public TrustFilterViewModel(
        IContentTrustService contentTrustService,
        IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
        IIncidentReporter incidentReporter)
    {
        ArgumentNullException.ThrowIfNull(contentTrustService);
        ArgumentNullException.ThrowIfNull(guardrailsOptions);
        ArgumentNullException.ThrowIfNull(incidentReporter);
        _contentTrustService = contentTrustService;
        _guardrailsOptions = guardrailsOptions;
        _incidentReporter = incidentReporter;
    }

    /// <summary>
    /// Gets the validated suspicious spans for human review only.
    /// </summary>
    public ObservableCollection<string> SuspiciousIndicators { get; } = [];

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScreenAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        _lastScreeningFailureWasTimeout = false;
        try
        {
            var options = _guardrailsOptions.CurrentValue;
            if (Input.Length <= options.MaxContentChars)
            {
                var trust = await ScreenWithFallbackAsync(
                        Input,
                        "Trust filter screening",
                        cancellationToken)
                    .ConfigureAwait(true);
                ApplyTrustResult(
                    Input,
                    trust,
                    options,
                    "Original",
                    ReturnBase64,
                    Input.Length,
                    Input.Length,
                    trust.SuspiciousIndicators);
                return;
            }

            ContentSummaryResult summary;
            try
            {
                summary = await _contentTrustService
                    .SummarizeAsync(
                        Input,
                        options.MaxSummaryChars,
                        cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (GuardrailSummarizationException exception)
            {
                await ReportAsync(exception, "Trust filter summarization")
                    .ConfigureAwait(true);
                ClearUnavailableResult(
                    "SummarizationFailed",
                    "Safe complete-source reduction failed. No source or partial summary is displayed.");
                return;
            }

            if (summary.Status is not (
                    ContentSummaryStatus.Summarized or
                    ContentSummaryStatus.SummaryTruncated))
            {
                ClearUnavailableResult(
                    summary.Status.ToString(),
                    DescribeUnavailableSummary(summary.Status));
                ProviderAndLatency =
                    $"{summary.ProviderName} · {summary.LatencyMs} ms";
                LengthDetails =
                    $"Source: {summary.OriginalCharacterCount} characters · Summary: 0 characters";
                return;
            }

            var finalTrust = await ScreenWithFallbackAsync(
                    summary.Summary,
                    "Trust filter final-summary screening",
                    cancellationToken)
                .ConfigureAwait(true);
            var indicators = UnionIndicators(
                summary.SuspiciousIndicators,
                finalTrust.SuspiciousIndicators);
            if (finalTrust.Screened)
            {
                finalTrust = finalTrust with
                {
                    TrustScore = Math.Min(
                        summary.SourceTrustScore
                            ?? throw new InvalidOperationException(
                                "A successful summary must contain a source trust score."),
                        finalTrust.TrustScore
                            ?? throw new InvalidOperationException(
                                "A screened final summary must contain a trust score.")),
                    SuspiciousIndicators = indicators,
                };
            }

            var truncated =
                summary.Status == ContentSummaryStatus.SummaryTruncated;
            ApplyTrustResult(
                summary.Summary,
                finalTrust,
                options,
                summary.Status.ToString(),
                ReturnBase64 || truncated,
                summary.OriginalCharacterCount,
                summary.SummaryCharacterCount,
                indicators);
            ProviderAndLatency =
                $"{finalTrust.ProviderName} · {summary.LatencyMs + finalTrust.LatencyMs} ms";
            Explanation = truncated
                ? "The AI-generated summary exceeded its delivery maximum, was shortened in C#, and is forced to marked Base64."
                : "The preview contains only the protected AI-generated complete-source summary.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ContentProcessingStatus = "Canceled";
            Explanation =
                "Screening was canceled. The previous result, if any, remains available.";
        }
        catch (Exception exception)
        {
            await ReportAsync(exception, "Trust filter")
                .ConfigureAwait(true);
            ClearUnavailableResult(
                "Failed",
                "Guardrail processing failed before a safe result could be produced.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ContentTrustResult> ScreenWithFallbackAsync(
        string content,
        string source,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _contentTrustService
                .ScreenAsync(content, ReturnBase64, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GuardrailScreeningException exception)
        {
            await ReportAsync(exception, source).ConfigureAwait(true);
            _lastScreeningFailureWasTimeout = exception.InnerException is TimeoutException;
            return exception.FallbackResult;
        }
    }

    private void ApplyTrustResult(
        string deliveryInput,
        ContentTrustResult trust,
        GuardrailsOptions options,
        string processingStatus,
        bool forceBase64,
        int originalLength,
        int deliveredLength,
        IReadOnlyList<string> indicators)
    {
        var disposition = ContentTrustPolicy.Decide(trust, options);
        if (forceBase64 && disposition == TrustDisposition.PassThrough)
        {
            disposition = TrustDisposition.EncodeBase64;
        }

        ResultContent = disposition switch
        {
            TrustDisposition.PassThrough =>
                trust.ContentEncoding == "none"
                    ? trust.Content
                    : ContentTrustEnvelope.WrapPlaintext(deliveryInput),
            TrustDisposition.EncodeBase64 =>
                trust.ContentEncoding == "base64"
                    ? trust.Content
                    : ContentTrustEnvelope.WrapBase64(deliveryInput),
            TrustDisposition.Withhold =>
                $"[external content withheld by guardrails: trust score {trust.TrustScore}]",
            _ => throw new InvalidOperationException(
                $"Unsupported trust disposition '{disposition}'."),
        };
        ContentEncoding = disposition == TrustDisposition.EncodeBase64
            ? "base64"
            : "none";
        TrustStatus = trust.Status.ToString();
        ContentProcessingStatus = processingStatus;
        TrustScore = trust.TrustScore;
        ScoreBadgeState = GetScoreBadgeState(trust.TrustScore, options);
        ProviderAndLatency = $"{trust.ProviderName} · {trust.LatencyMs} ms";
        LengthDetails =
            $"Source: {originalLength} characters · Delivered input: {deliveredLength} characters";
        SuspiciousIndicators.Clear();
        foreach (var indicator in indicators)
        {
            SuspiciousIndicators.Add(indicator);
        }

        Explanation = trust.Status switch
        {
            ContentTrustStatus.ScreeningDisabled =>
                "LLM scoring is disabled. Conservative marked-Base64 delivery was applied; protection remains active.",
            ContentTrustStatus.Failed =>
                _lastScreeningFailureWasTimeout
                    ? $"Screening timed out after {options.TimeoutSeconds} seconds. The content was treated with caution and delivered only as marked Base64."
                    : "Screening failed. The content was treated with caution and delivered only as marked Base64.",
            _ when disposition == TrustDisposition.Withhold =>
                "The score is below the caution threshold, so the external content was withheld.",
            _ when disposition == TrustDisposition.EncodeBase64 =>
                "The content is delivered as marked Base64 and remains untrusted after decoding.",
            _ =>
                "The content is clean enough for marked plaintext delivery, but remains untrusted data.",
        };
    }

    private void ClearUnavailableResult(
        string processingStatus,
        string explanation)
    {
        TrustStatus = "Not screened";
        ContentProcessingStatus = processingStatus;
        TrustScore = null;
        ScoreBadgeState = "Unknown";
        ResultContent = string.Empty;
        ContentEncoding = "none";
        SuspiciousIndicators.Clear();
        Explanation = explanation;
    }

    private async Task ReportAsync(Exception exception, string source)
    {
        _ = await _incidentReporter
            .ReportAsync(exception, source, CancellationToken.None)
            .ConfigureAwait(true);
    }

    private static string GetScoreBadgeState(
        int? score,
        GuardrailsOptions options)
    {
        if (score is null)
        {
            return "Unknown";
        }

        if (score >= options.CleanThreshold)
        {
            return "Clean";
        }

        return score >= options.CautionThreshold ? "Caution" : "Low";
    }

    private static IReadOnlyList<string> UnionIndicators(
        IEnumerable<string> first,
        IEnumerable<string> second)
    {
        var result = new List<string>();
        foreach (var indicator in first.Concat(second))
        {
            if (result.Count >= 20)
            {
                break;
            }

            if (!result.Contains(indicator, StringComparer.Ordinal))
            {
                result.Add(indicator);
            }
        }

        return result;
    }

    private static string DescribeUnavailableSummary(
        ContentSummaryStatus status) =>
        status switch
        {
            ContentSummaryStatus.LlmProcessingDisabled =>
                "LLM guardrails processing is disabled. Oversized source content is not displayed.",
            ContentSummaryStatus.SummarizationDisabled =>
                "Oversized-content summarization is disabled. Source content is not displayed.",
            ContentSummaryStatus.SourceTooLarge =>
                "The source exceeds the safe complete-source summarization limit and was not partially processed.",
            _ => "Safe reduction is unavailable.",
        };
}
