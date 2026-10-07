using System.Diagnostics;
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
    "Workflow-only Gmail classification using the global decision engine and deterministic reports.", 110,
    Activation = ToolActivationKind.Contextual, RegisterTasklessProjection = false)]
public sealed class GmailDecisionTool(
    ToolExecutionContext context, IGmailConnector gmail,
    IOptionsMonitor<GmailDecisionOptions> options, IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
    TimeProvider timeProvider, ILogger<GmailDecisionTool> logger, IDecisionEvaluationService evaluationService)
{
    /// <summary>Fetches a bounded batch once, validates native scores, and returns source-free coverage.</summary>
    [Tool("gmailDecision.readClassify", "Workflow-only Gmail triage using the global decision engine; local raw source, no second opinions or fallback, and source-free coverage.", 100, WorkflowOnly = true)]
    public async Task<string> ReadClassifyAsync(
        [ToolArgument(WorkflowArgumentKind.Object, "One to eight named choice, noul, or ordered score questions.", IsLongText = true, Example = GmailDecisionDefaults.QuestionsJson)] JsonElement questions,
        [ToolArgument(WorkflowArgumentKind.Object, "Category labels and attention ranks, with optional reply and priority question mappings.", IsLongText = true, Example = GmailDecisionDefaults.ReportMappingJson)] JsonElement reportMapping,
        [ToolArgument(WorkflowArgumentKind.Integer, "Explicit migration acknowledgement: version 2 uses only the global engine and no secondary-engine settings.", Minimum = 2, Maximum = 2, Example = "2")] int contractVersion,
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Gmail selection period.", AllowedValues = ["Today", "Unread", "Recent"], Example = "Recent")] string selection = "Recent",
        [ToolArgument(WorkflowArgumentKind.Integer, "Local calendar days before today; valid only with Recent.", Minimum = 0, Maximum = 3650, Example = "1")] int? daysBack = null,
        [ToolArgument(WorkflowArgumentKind.Integer, "Maximum selected messages within Settings and protection limits.", Minimum = 1, Maximum = 500, Example = "100")] int? maxMessages = null,
        [ToolArgument(WorkflowArgumentKind.Object, "Acceptance defaults and per-question or per-choice overrides.", IsLongText = true, Example = GmailDecisionDefaults.AcceptanceJson)] JsonElement? acceptance = null,
        [ToolArgument(WorkflowArgumentKind.Object, "Retired compatibility field. Remove explicitly when migrating to contractVersion 2; second opinions are unavailable.", IsLongText = true, Example = "{\"enabled\":true,\"maxPercent\":100,\"maxCalls\":0}")] JsonElement? arbitration = null,
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Retired compatibility field. Remove explicitly for contractVersion 2; selected-engine failure is unresolved coverage.", AllowedValues = ["LightweightLlmFallback", "Fail"], Example = "LightweightLlmFallback")] string? unavailableBehavior = null,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional shorter complete operation deadline.", Minimum = 10, Maximum = 3600, Example = "120")] int? batchTimeoutSeconds = null,
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Compatibility marker. Omit for the global engine; migrate legacy Hybrid/LlmOnly contracts explicitly.", AllowedValues = ["Global", "Hybrid", "LlmOnly"], Example = "Global", IsExcludedFromWorkflowTextEditor = true)] string classificationMode = "Global",
        CancellationToken cancellationToken = default)
    {
        EnsureWorkflow();
        if (contractVersion != 2)
            throw new WorkflowValidationException("Gmail decision contractVersion must be 2. Review legacy mode, arbitration, fallback, and score gates before migrating this workflow.");
        if (evaluationService is null) throw new WorkflowValidationException("The common decision service is unavailable.");
        var snapshot = context.DecisionSnapshot ?? evaluationService.Capture()
            ?? throw new WorkflowValidationException("The decision configuration cannot be captured.");
        var settings = snapshot.Settings.ApplyTo(options.CurrentValue with { });
        if (!settings.Enabled) throw new WorkflowValidationException("Enable Gmail decision reader in Settings before using this tool.");
        GmailDecisionMigration.Validate(classificationMode, arbitration, unavailableBehavior, snapshot.Engine, acceptance);
        GmailDecisionMigration.ValidateAcceptance(acceptance, settings);
        var questionSet = DecisionQuestionSet.Parse(questions);
        var policy = DecisionAcceptancePolicy.Parse(questionSet, settings with { ArbitrationEnabled = false },
            acceptance ?? default, reportMapping, default);
        if (snapshot.Engine == "CentralLlm" && policy.Mapping.ReplyTrueAtPercent != 50)
            throw new WorkflowValidationException("Native replyTrueAtPercent report thresholds require migration. Central Boolean answers are semantic values, not probabilities.");
        if (policy.PerQuestion.Values.Any(r => r.AlwaysArbitrateChoices.Count > 0))
            throw new WorkflowValidationException("Always-arbitrate rules require migration; the global contract calls only one engine.");
        if (selection is not ("Today" or "Unread" or "Recent") || daysBack is < 0 or > 3650 || (selection != "Recent" && daysBack.HasValue))
            throw new WorkflowValidationException("Invalid Gmail selection or daysBack.");
        if (maxMessages is < 1 || maxMessages > settings.MaxMessagesPerOperation ||
            batchTimeoutSeconds is < 10 || batchTimeoutSeconds > settings.BatchTimeoutSeconds)
            throw new WorkflowValidationException("Workflow bounds may shorten, never extend Settings limits.");
        if (settings.MaxMessagesPerOperation is < 1 or > 500 || settings.BatchTimeoutSeconds is < 10 or > 3600)
            throw new WorkflowValidationException("Invalid Gmail collection limits.");
        var maximum = maxMessages ?? settings.MaxMessagesPerOperation;
        var protectionLimit = guardrailsOptions.CurrentValue.MaxGmailMessagesPerOperation;
        if (protectionLimit > 0) maximum = Math.Min(maximum, protectionLimit);
        if (snapshot.Engine == "CentralLlm" && snapshot.Central?.IsLocal == false)
            throw new WorkflowValidationException("LocalSourceRequired: raw Gmail content cannot be sent to the configured cloud provider. Select a local central provider or a native decision model in Settings.");
        using var operationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(batchTimeoutSeconds ?? settings.BatchTimeoutSeconds), timeProvider);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operationTimeout.Token);
        var timer = Stopwatch.StartNew();
        var batch = await gmail.GetDecisionBatchAsync(selection, daysBack ?? 1, maximum, deadline.Token).ConfigureAwait(false);
        var mails = batch.Messages;
        if (mails.Count > maximum || mails.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != mails.Count ||
            mails.Any(m => string.IsNullOrWhiteSpace(m.Id) || m.Id.Length > 256))
            throw new WorkflowValidationException("Gmail collection exceeded its bound or returned duplicate/invalid identities.");
        var result = new GmailDecisionResult
        {
            SchemaVersion = 2, Engine = snapshot.Engine, Capabilities = new(snapshot.Engine == "NativeDecision"),
            AcceptanceBasis = snapshot.Engine == "NativeDecision" ? "nativeThresholds" : "validatedChoice",
            ClassificationMode = "Global", Provider = snapshot.Provider,
            Selection = new(selection, selection == "Recent" ? daysBack ?? 1 : null),
            CollectedAt = timeProvider.GetLocalNow(), CollectionPartial = batch.CollectionPartial,
            CollectionMs = timer.Elapsed.TotalMilliseconds, EffectiveModels = new() { ["decision"] = snapshot.Model },
            QuestionDefinitions = questionSet.Questions.ToDictionary(p => p.Key, p => new AnswerDefinition(p.Value.Type, p.Value.Keys)),
            ReportMapping = policy.Mapping,
            Messages = mails.Select(m => new DecisionMailResult
            {
                Id = m.Id, Sender = GmailDecisionProjection.Bound(m.From, 256), Subject = GmailDecisionProjection.Bound(m.Subject, 300),
                ReceivedAt = m.ReceivedAt, GmailUrl = GmailUrl(m.Id),
            }).ToList(),
        };
        for (var index = 0; index < mails.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = result.Messages[index];
            if (deadline.IsCancellationRequested) { row.ArbitrationReasons.Add("NotProcessedBeforeDeadline"); continue; }
            row.Processed = true;
            var projection = GmailDecisionProjection.CreateForLlm(mails[index], settings.MaxProjectionCharacters, int.MaxValue);
            row.Projection = new(projection.CurrentContentTruncated, projection.QuotedHistoryRemoved)
            { LlmContentTruncated = projection.LlmContentTruncated };
            try
            {
                var response = await evaluationService.EvaluateQuestionsAsync(snapshot, projection.LlmSource, questionSet, true, deadline.Token).ConfigureAwait(false);
                row.Timing.GateWaitMs = response.Timing.GateWaitMs;
                row.Timing.ClassificationMs = response.Timing.InferenceMs;
                row.Timing.ColdOrReload = response.Timing.Cold == true;
                row.ArbitrationReasons.AddRange(response.Reasons);
                if (response.NativePayload is { } payload)
                {
                    var scores = questionSet.ValidateNative(payload, policy.Mapping.ReplyTrueAtPercent);
                    row.Answers = scores.Answers;
                    row.DecisionScores = scores.Scores;
                    row.DecisionSource = "decision";
                    row.ArbitrationReasons.AddRange(policy.Evaluate(scores, projection.CurrentContentTruncated));
                    row.Timing.DecisionMs = response.Timing.InferenceMs;
                    row.Timing.ClassificationMs = 0;
                }
                else if (response.Answers is { } answers)
                {
                    row.Answers = questionSet.ValidateLlmAnswers(answers);
                    row.DecisionSource = "directLlm";
                    if (projection.CurrentContentTruncated) row.ArbitrationReasons.Add("CurrentContentTruncated");
                }
                if (row.Answers.Count > 0)
                {
                    row.Category = (string)row.Answers[policy.Mapping.CategoryQuestion];
                    Resolve(row, row.ArbitrationReasons.Count > 0 || policy.RemainsUncertain(row.Answers));
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { row.ArbitrationReasons.Add("OperationDeadline"); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        result.Counts = Count(result.Messages);
        if (batch.CollectionPartial) result.Warnings.Add("CollectionPartial");
        if (result.Messages.Any(r => r.Status != "Classified")) result.Warnings.Add("HumanReviewRequired");
        result.Status = deadline.IsCancellationRequested ? "Partial" : result.Warnings.Count > 0 ? "CompletedWithWarnings" : "Completed";
        result.TotalMs = timer.Elapsed.TotalMilliseconds;
        logger.LogInformation("Gmail decision run {RunId}, step {StepId}, engine {Engine}, model {Model}: {Status}, collected {Count}, unresolved {Unresolved}.",
            context.RunId, context.StepId, snapshot.Engine, snapshot.Model, result.Status, result.Counts.Collected, result.Counts.Unresolved);
        return SerializeBounded(result);
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

    internal static bool TryIdentity(IReadOnlyDictionary<string, string> models, string model, out string? identity) =>
        DecisionModelIdentity.TryGet(models, model, out identity);

    /// <summary>Compares validated loopback endpoint identities with localhost normalization.</summary>
    public static bool SameEndpoint(string a, string b)
    {
        return DecisionModelIdentity.SameEndpoint(a, b);
    }
}
