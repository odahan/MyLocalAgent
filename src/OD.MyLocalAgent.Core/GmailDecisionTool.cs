using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>Collects once and classifies sequentially inside an explicitly authorized workflow.</summary>
[ToolGroup("gmailDecision", "Gmail decision reader",
    "Workflow-only local Gmail classification with lightweight fallback and deterministic reports.", 110,
    Activation = ToolActivationKind.Contextual, RegisterTasklessProjection = false)]
public sealed class GmailDecisionTool(
    ToolExecutionContext context, IGmailConnector gmail,
    IOptionsMonitor<GmailDecisionOptions> options, IOptionsMonitor<OllamaOptions> ollamaOptions,
    IOptionsMonitor<KnowledgeOptions> knowledgeOptions, IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
    ILocalDecisionClient decisionClient, ILightweightMailClassifier classifier,
    DecisionModelResidencyCoordinator residency, TimeProvider timeProvider, ILogger<GmailDecisionTool> logger)
{
    /// <summary>Fetches a bounded batch once, validates native scores, and returns source-free coverage.</summary>
    [Tool("gmailDecision.readClassify", "Workflow-only Gmail triage using local decisions and isolated lightweight second opinions; returns structured coverage without mail bodies.", 100, WorkflowOnly = true)]
    public async Task<string> ReadClassifyAsync(
        [ToolArgument(WorkflowArgumentKind.Object, "One to eight named choice, noul, or ordered score questions.", IsLongText = true, Example = GmailDecisionDefaults.QuestionsJson)] JsonElement questions,
        [ToolArgument(WorkflowArgumentKind.Object, "Category labels and attention ranks, with optional reply and priority question mappings.", IsLongText = true, Example = GmailDecisionDefaults.ReportMappingJson)] JsonElement reportMapping,
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Gmail selection period.", AllowedValues = ["Today", "Unread", "Recent"], Example = "Recent")] string selection = "Recent",
        [ToolArgument(WorkflowArgumentKind.Integer, "Local calendar days before today; valid only with Recent.", Minimum = 0, Maximum = 3650, Example = "1")] int? daysBack = null,
        [ToolArgument(WorkflowArgumentKind.Integer, "Maximum selected messages within Settings and protection limits.", Minimum = 1, Maximum = 500, Example = "100")] int? maxMessages = null,
        [ToolArgument(WorkflowArgumentKind.Object, "Acceptance defaults and per-question or per-choice overrides.", IsLongText = true, Example = GmailDecisionDefaults.AcceptanceJson)] JsonElement? acceptance = null,
        [ToolArgument(WorkflowArgumentKind.Object, "Second-opinion enablement, budget, and typed cross-question conflicts.", IsLongText = true, Example = "{\"enabled\":true,\"maxPercent\":100,\"maxCalls\":0}")] JsonElement? arbitration = null,
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Technical decision-provider unavailability policy.", AllowedValues = ["LightweightLlmFallback", "Fail"], Example = "LightweightLlmFallback")] string? unavailableBehavior = null,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional shorter complete operation deadline.", Minimum = 10, Maximum = 3600, Example = "120")] int? batchTimeoutSeconds = null,
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Hybrid native decisions with arbitration, or one direct local LLM classification per mail.", AllowedValues = ["Hybrid", "LlmOnly"], Example = "LlmOnly")] string classificationMode = "Hybrid",
        CancellationToken cancellationToken = default)
    {
        EnsureWorkflow();
        var settings = options.CurrentValue with { };
        var ollama = ollamaOptions.CurrentValue with { };
        if (!settings.Enabled) throw new WorkflowValidationException("Enable Gmail decision reader in Settings before using this tool.");
        if (!GmailDecisionOptions.IsValid(settings) || !LocalDecisionEndpoint.IsValid(ollama.Endpoint))
            throw new WorkflowValidationException("Gmail decision settings require valid loopback-only endpoints and bounded policies.");
        var questionSet = DecisionQuestionSet.Parse(questions);
        var policy = DecisionAcceptancePolicy.Parse(questionSet, settings, acceptance ?? default, reportMapping, arbitration ?? default);
        if (classificationMode is not ("Hybrid" or "LlmOnly"))
            throw new WorkflowValidationException("Unknown Gmail classification mode.");
        var llmOnly = classificationMode == "LlmOnly";
        var sourceBudget = llmOnly ? GmailDecisionProjection.LlmBudget(questionSet) : GmailDecisionProjection.MailBudget(questionSet);
        if (selection is not ("Today" or "Unread" or "Recent") || daysBack is < 0 or > 3650 || (selection != "Recent" && daysBack.HasValue))
            throw new WorkflowValidationException("Invalid Gmail selection or daysBack for the selection.");
        if (maxMessages is < 1 || maxMessages > settings.MaxMessagesPerOperation ||
            batchTimeoutSeconds is < 10 || batchTimeoutSeconds > settings.BatchTimeoutSeconds)
            throw new WorkflowValidationException("Workflow bounds may shorten, never extend Settings limits.");
        var unavailable = unavailableBehavior ?? settings.UnavailableBehavior;
        if (unavailable is not ("Fail" or "LightweightLlmFallback")) throw new WorkflowValidationException("Unknown unavailable behavior.");
        var decisionEndpoint = settings.DecisionEndpoint ?? ollama.Endpoint;
        var arbitrationModel = settings.ArbitrationModel ?? ollama.Model;
        var fallbackModel = settings.FallbackModel ?? arbitrationModel;
        var embeddingModel = knowledgeOptions.CurrentValue.EmbeddingModel;
        var maximum = maxMessages ?? settings.MaxMessagesPerOperation;
        var protectionLimit = guardrailsOptions.CurrentValue.MaxGmailMessagesPerOperation;
        if (protectionLimit > 0) maximum = Math.Min(maximum, protectionLimit);
        using var operationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(batchTimeoutSeconds ?? settings.BatchTimeoutSeconds), timeProvider);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operationTimeout.Token);
        var operationId = Guid.NewGuid().ToString("N");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(questions.GetRawText())));
        var timer = Stopwatch.StartNew();
        var circuit = false;
        var invalidConsecutive = 0;
        var warnings = new List<string>();
        var cleanupAllowed = false;
        IDisposable? ownership = null;
        GmailDecisionResult? completed = null;
        try
        {
            if (!llmOnly)
            {
                try
                {
                    using var catalogTimeout = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                    catalogTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                    IReadOnlyDictionary<string, string> identities;
                    try { identities = await decisionClient.GetModelIdentitiesAsync(decisionEndpoint, catalogTimeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
                    { throw new LocalDecisionException("CatalogTimeout", true); }
                    if (!TryIdentity(identities, settings.DecisionModel, out var decisionIdentity))
                        throw new LocalDecisionException("ModelMissing");
                    if (SameEndpoint(decisionEndpoint, ollama.Endpoint))
                    {
                        foreach (var shared in new[] { ollama.Model, arbitrationModel, fallbackModel, embeddingModel })
                        {
                            if (TryIdentity(identities, shared, out var identity) && identity == decisionIdentity)
                                throw new WorkflowValidationException("Decision model shares an installed identity with a chat or embedding model; cleanup would unload a primary model.");
                        }
                    }
                    cleanupAllowed = true;
                    ownership = await residency.BeginOperationAsync(decisionEndpoint, settings.DecisionModel, deadline.Token).ConfigureAwait(false);
                }
                catch (LocalDecisionException e)
                {
                    if (unavailable == "Fail") throw;
                    circuit = true;
                    warnings.Add(e.Code);
                }
            }
            var collectionTimer = Stopwatch.StartNew();
            logger.LogInformation("Gmail decision {OperationId}: collection started for task {TaskId}.", operationId, context.TaskId);
            var batch = await gmail.GetDecisionBatchAsync(selection, daysBack ?? 1, maximum, deadline.Token).ConfigureAwait(false);
            var mails = batch.Messages;
            if (mails.Count > maximum || mails.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != mails.Count || mails.Any(m => string.IsNullOrWhiteSpace(m.Id) || m.Id.Length > 256))
                throw new WorkflowValidationException("Gmail collection exceeded its bound or returned duplicate/invalid message identities.");
            var result = new GmailDecisionResult
            {
                Selection = new DecisionSelection(selection, selection == "Recent" ? daysBack ?? 1 : null),
                CollectedAt = timeProvider.GetLocalNow(), CollectionPartial = batch.CollectionPartial,
                ClassificationMode = classificationMode,
                EffectiveModels = llmOnly ? new() { ["classification"] = fallbackModel } :
                    new() { ["decision"] = settings.DecisionModel, ["arbitration"] = arbitrationModel, ["fallback"] = fallbackModel },
                QuestionDefinitions = questionSet.Questions.ToDictionary(p => p.Key, p => new AnswerDefinition(p.Value.Type, p.Value.Keys)),
                ReportMapping = policy.Mapping, Warnings = warnings, CollectionMs = collectionTimer.Elapsed.TotalMilliseconds,
                Messages = mails.Select(m => new DecisionMailResult
                {
                    Id = m.Id, Sender = GmailDecisionProjection.Bound(m.From, 256), Subject = GmailDecisionProjection.Bound(m.Subject, 300),
                    ReceivedAt = m.ReceivedAt, GmailUrl = GmailUrl(m.Id),
                }).ToList(),
            };
            var cap = DecisionAcceptancePolicy.ArbitrationCap(mails.Count, policy.Arbitration.MaxPercent!.Value, policy.Arbitration.MaxCalls!.Value);
            var attempted = 0;
            for (var index = 0; index < mails.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = result.Messages[index];
                if (deadline.IsCancellationRequested)
                {
                    row.ArbitrationReasons.Add("NotProcessedBeforeDeadline");
                    continue;
                }
                row.Processed = true;
                var projection = llmOnly
                    ? GmailDecisionProjection.CreateForLlm(mails[index], settings.MaxProjectionCharacters, sourceBudget)
                    : GmailDecisionProjection.Create(mails[index], settings.MaxProjectionCharacters, sourceBudget);
                row.Projection = new(projection.CurrentContentTruncated, projection.QuotedHistoryRemoved)
                {
                    DecisionContentTruncated = llmOnly ? null : projection.DecisionContentTruncated,
                    LlmContentTruncated = projection.LlmContentTruncated,
                };
                try
                {
                    DecisionScores? native = null;
                    var useFallback = circuit || llmOnly;
                    if (circuit) row.ArbitrationReasons.Add("DecisionCircuitOpen");
                    if (!circuit && !llmOnly)
                    {
                        try
                        {
                            var request = new LocalDecisionRequest(decisionEndpoint, settings.DecisionModel, projection.DecisionSource, questionSet.Json,
                                settings.DecisionWarmTimeoutSeconds, settings.GateWaitTimeoutSeconds, settings.KeepAliveSeconds);
                            LocalDecisionResponse response;
                            try { response = await decisionClient.DecideAsync(request, settings.DecisionColdTimeoutSeconds, deadline.Token).ConfigureAwait(false); }
                            catch (LocalDecisionException e) when (e.Transient && !deadline.IsCancellationRequested)
                            {
                                row.Timing.GateWaitMs += e.GateWaitMs;
                                row.Timing.DecisionMs += e.InferenceMs;
                                row.ArbitrationReasons.Add("TransientRetry");
                                response = await decisionClient.DecideAsync(request, settings.DecisionColdTimeoutSeconds, deadline.Token).ConfigureAwait(false);
                            }
                            row.Timing.GateWaitMs += response.GateWaitMs;
                            row.Timing.DecisionMs += response.InferenceMs;
                            row.Timing.ColdOrReload = response.Cold;
                            native = questionSet.ValidateNative(response.Payload, policy.Mapping.ReplyTrueAtPercent);
                            invalidConsecutive = 0;
                            row.Answers = native.Answers;
                            row.DecisionScores = native.Scores;
                            row.DecisionSource = "decision";
                            row.Category = (string)native.Answers[policy.Mapping.CategoryQuestion];
                            row.ArbitrationReasons = policy.Evaluate(native, projection.CurrentContentTruncated);
                            logger.LogInformation("Gmail decision acceptance {OperationId}, mail index {MailIndex}: selected native scores {NativeScores}, inclusive default rules {DefaultRules}, question/choice overrides {Overrides}, exact failures {Reasons}.",
                                operationId, index, JsonSerializer.Serialize(native.Scores, DecisionContractJson.Options),
                                JsonSerializer.Serialize(policy.Defaults, DecisionContractJson.Options),
                                JsonSerializer.Serialize(policy.PerQuestion, DecisionContractJson.Options), string.Join(",", row.ArbitrationReasons));
                            Trace(operationId, index, hash, settings.DecisionModel, projection, response, "decision", "Valid", row.ArbitrationReasons);
                        }
                        catch (Exception e) when (e is LocalDecisionException or DecisionResponseException or JsonException or KeyNotFoundException or InvalidOperationException or WorkflowValidationException)
                        {
                            var code = FailureCode(e);
                            TraceFailure(operationId, index, "decision", e);
                            if (e is LocalDecisionException failed)
                            {
                                row.Timing.GateWaitMs += failed.GateWaitMs;
                                row.Timing.DecisionMs += failed.InferenceMs;
                            }
                            var invalid = e is not LocalDecisionException || code is "InvalidJson" or "EmptyResponse";
                            invalidConsecutive = invalid ? invalidConsecutive + 1 : 0;
                            circuit = !invalid || invalidConsecutive >= 2;
                            row.ArbitrationReasons.Add(code);
                            if (unavailable == "Fail") throw new LocalDecisionException(code);
                            useFallback = true;
                            if (!warnings.Contains("DecisionProviderDegraded")) warnings.Add("DecisionProviderDegraded");
                            logger.LogWarning("Gmail decision {OperationId}, mail index {MailIndex}: {FailureCode}; circuit {CircuitOpen}.", operationId, index, code, circuit);
                        }
                    }
                    if (useFallback)
                    {
                        var response = await classifier.ClassifyAsync(new LocalDecisionRequest(ollama.Endpoint, fallbackModel, projection.LlmSource,
                            questionSet.Json, settings.FallbackTimeoutSeconds, settings.GateWaitTimeoutSeconds, 0), questionSet, deadline.Token).ConfigureAwait(false);
                        row.Answers = questionSet.ValidateLlmAnswers(response.Payload);
                        row.Category = (string)row.Answers[policy.Mapping.CategoryQuestion];
                        row.DecisionSource = llmOnly ? "directLlm" : "lightweightLlm";
                        row.Timing.GateWaitMs += response.GateWaitMs;
                        if (llmOnly) row.Timing.ClassificationMs = response.InferenceMs;
                        else row.Timing.FallbackMs = response.InferenceMs;
                        Resolve(row, policy.RemainsUncertain(row.Answers));
                        Trace(operationId, index, hash, fallbackModel, projection, response, llmOnly ? "classification" : "fallback", "Valid", row.ArbitrationReasons);
                    }
                    else if (native is not null && row.ArbitrationReasons.Count == 0)
                        Resolve(row, false);
                    else if (native is not null)
                    {
                        Resolve(row, true);
                        if (policy.Arbitration.Enabled == true && attempted < cap)
                        {
                            attempted++;
                            row.ArbitrationAttempted = true;
                            var response = await classifier.ClassifyAsync(new LocalDecisionRequest(ollama.Endpoint, arbitrationModel, projection.LlmSource,
                                questionSet.Json, settings.ArbitrationTimeoutSeconds, settings.GateWaitTimeoutSeconds, 0), questionSet, deadline.Token).ConfigureAwait(false);
                            var opinion = questionSet.ValidateLlmAnswers(response.Payload);
                            var original = (string)native.Answers[policy.Mapping.CategoryQuestion];
                            var second = (string)opinion[policy.Mapping.CategoryQuestion];
                            row.Disagreement = native.Answers.Any(p => !Equals(p.Value, opinion[p.Key]));
                            row.Answers = opinion;
                            if (policy.Mapping.Categories[original].AttentionRank >= 1 && policy.Mapping.Categories[second].AttentionRank == 0)
                            {
                                row.Answers[policy.Mapping.CategoryQuestion] = original;
                                row.ArbitrationReasons.Add("VisibilityProtectionOverride");
                            }
                            row.Category = (string)row.Answers[policy.Mapping.CategoryQuestion];
                            row.DecisionSource = "arbitrated";
                            row.Timing.GateWaitMs += response.GateWaitMs;
                            row.Timing.ArbitrationMs = response.InferenceMs;
                            Resolve(row, policy.RemainsUncertain(row.Answers) ||
                                row.ArbitrationReasons.Contains("VisibilityProtectionOverride"));
                            Trace(operationId, index, hash, arbitrationModel, projection, response, "arbitration", "Valid", row.ArbitrationReasons);
                        }
                        else row.ArbitrationReasons.Add(policy.Arbitration.Enabled == true ? "ArbitrationCap" : "ArbitrationDisabled");
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
                { row.ArbitrationReasons.Add("OperationDeadline"); }
                catch (Exception e) when (e is LocalDecisionException or DecisionResponseException or JsonException or KeyNotFoundException or InvalidOperationException or WorkflowValidationException)
                {
                    if (unavailable == "Fail" && row.DecisionScores.Count == 0) throw;
                    row.ArbitrationReasons.Add(FailureCode(e));
                    TraceFailure(operationId, index, llmOnly ? "classification" : row.ArbitrationAttempted ? "arbitration" : "fallback", e);
                    if (e is LocalDecisionException failed)
                    {
                        row.Timing.GateWaitMs += failed.GateWaitMs;
                        if (row.ArbitrationAttempted) row.Timing.ArbitrationMs += failed.InferenceMs;
                        else if (llmOnly) row.Timing.ClassificationMs += failed.InferenceMs;
                        else row.Timing.FallbackMs += failed.InferenceMs;
                    }
                    logger.LogWarning("Gmail decision {OperationId}, mail index {MailIndex}: classification failed ({FailureCode}).", operationId, index, FailureCode(e));
                }
                logger.LogInformation("Gmail decision {OperationId}: processed {Processed}/{Total}, status {MailStatus}, source {DecisionSource}.", operationId, index + 1, mails.Count, row.Status, row.DecisionSource);
            }
            cancellationToken.ThrowIfCancellationRequested();
            result.Counts = Count(result.Messages);
            if (result.CollectionPartial) warnings.Add("CollectionPartial");
            if (result.Messages.Any(r => r.Status != "Classified")) warnings.Add("HumanReviewRequired");
            if (mails.Count > 0 && 100d * (result.Counts.Provisional + result.Counts.Unresolved) / mails.Count > settings.QualityWarningPercent)
                warnings.Add("ClassificationQualityDegraded");
            result.Status = deadline.IsCancellationRequested ? "Partial" : warnings.Count > 0 ? "CompletedWithWarnings" : "Completed";
            result.TotalMs = timer.Elapsed.TotalMilliseconds;
            completed = result;
        }
        finally
        {
            ownership?.Dispose();
            if (cleanupAllowed)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await decisionClient.UnloadAsync(decisionEndpoint, settings.DecisionModel, cleanup.Token).ConfigureAwait(false); }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    logger.LogWarning("Gmail decision {OperationId}: decision-model cleanup failed ({FailureCode}).", operationId, FailureCode(e));
                    TraceFailure(operationId, null, "cleanup", e);
                    warnings.Add("MemoryCleanupFailure");
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (completed is null) throw new WorkflowValidationException("Gmail decision operation did not produce coverage.");
        if (completed.Status == "Completed" && warnings.Count > 0) completed.Status = "CompletedWithWarnings";
        completed.TotalMs = timer.Elapsed.TotalMilliseconds;
        return SerializeBounded(completed);
    }

    /// <summary>Validates a previous reader output and creates Markdown locally without file or model calls.</summary>
    [Tool("gmailDecision.renderReport", "Workflow-only deterministic Markdown report from validated Gmail decision coverage; no model, Gmail, or file calls.", 200, WorkflowOnly = true)]
    public Task<string> RenderReportAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Complete JSON output of gmailDecision.readClassify.", IsLongText = true, Example = "{{mailTriage.output}}")] string source,
        [ToolArgument(WorkflowArgumentKind.String, "Escaped report title, at most 200 characters.", MaximumLength = 200, Example = "Gmail watch")] string title = "Gmail watch",
        [ToolArgument(WorkflowArgumentKind.Boolean, "Show rank-zero categories; hidden rows remain explicitly counted.", Example = "true")] bool includeNoise = true,
        CancellationToken cancellationToken = default)
    {
        EnsureWorkflow();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GmailDecisionReportRenderer.Render(source, title, includeNoise));
    }

    internal static DecisionCounts Count(IReadOnlyList<DecisionMailResult> rows) => new()
    {
        Collected = rows.Count, Processed = rows.Count(r => r.Processed), Classified = rows.Count(r => r.Status == "Classified"),
        Provisional = rows.Count(r => r.Status == "Provisional"), Unresolved = rows.Count(r => r.Status == "Unresolved"),
        DecisionAccepted = rows.Count(r => r.Status == "Classified" && r.DecisionSource == "decision"),
        ArbitrationAttempted = rows.Count(r => r.ArbitrationAttempted),
        ArbitrationResolved = rows.Count(r => r.Status == "Classified" && r.DecisionSource == "arbitrated"),
        LightweightFallbackResolved = rows.Count(r => r.Status == "Classified" && r.DecisionSource == "lightweightLlm"),
        DirectLlmResolved = rows.Count(r => r.Status == "Classified" && r.DecisionSource == "directLlm"),
    };

    internal static string SerializeBounded(GmailDecisionResult result)
    {
        string Serialize() => JsonSerializer.Serialize(result, DecisionContractJson.Options);
        var json = Serialize();
        if (Encoding.UTF8.GetByteCount(json) <= 512 * 1024) return json;
        foreach (var row in result.Messages) foreach (var score in row.DecisionScores.Values) score.Probabilities = null;
        result.ScoreDetailsOmitted = true;
        json = Serialize();
        if (Encoding.UTF8.GetByteCount(json) > 512 * 1024)
            throw new WorkflowValidationException("Decision result metadata exceeds the 512 KiB workflow bound; no mail rows were dropped.");
        return json;
    }

    private void EnsureWorkflow()
    {
        if (context.TaskId is null || context.Surface is not (ToolInvocationSurface.Workflow or ToolInvocationSurface.WorkflowTest))
            throw new WorkflowValidationException("Gmail decision tools require an authorized workflow task.");
    }

    private static void Resolve(DecisionMailResult row, bool provisional)
    {
        row.Status = provisional ? "Provisional" : "Classified";
        row.RequiresHumanReview = provisional;
    }

    internal static string? GmailUrl(string id) => Regex.IsMatch(id, "^[A-Fa-f0-9]{1,64}$", RegexOptions.CultureInvariant)
        ? "https://mail.google.com/mail/u/0/#all/" + id : null;

    private static string FailureCode(Exception e) => e switch
    {
        LocalDecisionException d => d.Code, DecisionResponseException d => d.Code,
        OperationCanceledException => "OperationDeadline", JsonException => "InvalidJson", _ => "InvalidResponse",
    };

    /// <summary>Records bounded structural failure diagnostics without exception messages or provider bodies.</summary>
    private void TraceFailure(string operation, int? index, string phase, Exception error)
    {
        logger.LogDebug("Gmail decision failure {OperationId}, task {TaskId}, run {RunId}, step {StepId}, mail index {MailIndex}, phase {Phase}, code {FailureCode}, exception type {ExceptionType}, stack {ExceptionStack}.",
            operation, context.TaskId, context.RunId, context.StepId, index, phase, FailureCode(error),
            error.GetType().Name, GmailDecisionProjection.Bound(error.StackTrace ?? string.Empty, 4000));
    }

    private void Trace(string operation, int index, string hash, string model, GmailDecisionProjection projection,
        LocalDecisionResponse response, string phase, string validation, List<string> reasons)
    {
        var source = phase == "decision" ? projection.DecisionSource : projection.LlmSource;
        logger.LogInformation(
            "Gmail decision trace {OperationId}, task {TaskId}, workflow run {RunId}, step {StepId}, mail index {MailIndex}, phase {Phase}, model {Model}, question hash {QuestionHash}, chars {ProjectionChars}, conservative tokens {TokenEstimate}, truncation {Truncated}, history removed {HistoryRemoved}, gate wait {GateWaitMs} ms, load+inference {InferenceMs} ms, cold/reload {Cold}, validation {Validation}, reasons {Reasons}, input tokens {InputTokens}, output tokens {OutputTokens}, finish {FinishReason}, response hash {ResponseHash}.",
            operation, context.TaskId, context.RunId, context.StepId, index, phase, model, hash, source.Length, GmailDecisionProjection.EstimateTokens(source),
            phase == "decision" ? projection.DecisionContentTruncated : projection.LlmContentTruncated,
            projection.QuotedHistoryRemoved, response.GateWaitMs, response.InferenceMs,
            response.Cold, validation, string.Join(",", reasons), response.InputTokens, response.OutputTokens, response.FinishReason,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response.Payload.GetRawText()))));
    }

    internal static bool TryIdentity(IReadOnlyDictionary<string, string> models, string model, out string? identity) =>
        models.TryGetValue(model.Contains(':') ? model : model + ":latest", out identity) || models.TryGetValue(model, out identity);

    /// <summary>Compares validated loopback endpoint identities with localhost normalization.</summary>
    public static bool SameEndpoint(string a, string b)
    {
        return LocalDecisionEndpoint.Identity(a) == LocalDecisionEndpoint.Identity(b);
    }
}
