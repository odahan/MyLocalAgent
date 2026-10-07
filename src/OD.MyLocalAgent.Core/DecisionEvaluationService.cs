using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>Reuses the native transport, execution gate and shared residency coordinator for one bounded decision.</summary>
public sealed class DecisionEvaluationService(
    IOptionsMonitor<DecisionOptions> options, IOptionsMonitor<OllamaOptions> ollamaOptions,
    IOptionsMonitor<KnowledgeOptions> knowledgeOptions,
    ILocalDecisionClient client, DecisionModelResidencyCoordinator residency,
    TimeProvider timeProvider, ILogger<DecisionEvaluationService> logger) : IDecisionEvaluationService
{
    /// <inheritdoc />
    public async Task<LocalDecisionResponse> ScoreAsync(LocalDecisionRequest request, int coldTimeoutSeconds,
        DecisionQuestionSet questions, CancellationToken cancellationToken)
    {
        var response = await client.DecideAsync(request, coldTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        questions.ValidateResponse(response.Payload);
        return response;
    }

    /// <inheritdoc />
    public async Task<DecisionEvaluationResult> EvaluateAsync(string source, string question, JsonElement choices,
        JsonElement? acceptance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.CurrentValue with { };
        if (!settings.Enabled) throw new WorkflowValidationException("Enable decision evaluation in Settings before using this tool.");
        var local = ollamaOptions.CurrentValue with { };
        var endpoint = settings.DecisionEndpoint ?? local.Endpoint;
        if (!DecisionOptions.IsValid(settings) || !LocalDecisionEndpoint.IsValid(endpoint))
            throw new WorkflowValidationException("Decision evaluation requires bounded settings and a loopback endpoint.");
        if (string.IsNullOrWhiteSpace(source)) throw new WorkflowValidationException("Decision source must be non-empty text.");
        var questions = DecisionEvaluateContract.Questions(question, choices);
        var rules = DecisionEvaluateContract.Acceptance(acceptance, settings);
        var timer = Stopwatch.StartNew();
        double gateWait = 0, inference = 0;
        bool? cold = null;
        DecisionEvaluationResult Result(string status, NativeDecisionScore? score, IReadOnlyList<string> reasons) =>
            new(1, status, score?.Choice, score?.Probabilities, score?.Top, score?.Margin, score?.Confidence,
                rules, reasons, settings.DecisionProvider, settings.DecisionModel,
                new(gateWait, inference, timer.Elapsed.TotalMilliseconds, cold));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(questions.Json.GetRawText())));
        var estimated = (long)Encoding.UTF8.GetByteCount(source) + Encoding.UTF8.GetByteCount(questions.Json.GetRawText()) + 400;
        if (estimated > settings.MaxInputTokens)
        {
            logger.LogInformation("Decision model {Model}, question hash {QuestionHash}, status unavailable, reason InputTooLarge.", settings.DecisionModel, hash);
            return Result("unavailable", null, ["InputTooLarge"]);
        }
        using var operationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.OperationTimeoutSeconds), timeProvider);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operationTimeout.Token);
        IDisposable? owner = null;
        var cleanupAllowed = false;
        DecisionEvaluationResult result;
        try
        {
            using var catalogTimeout = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            catalogTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            IReadOnlyDictionary<string, string> identities;
            try { identities = await client.GetModelIdentitiesAsync(endpoint, catalogTimeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
            { throw new LocalDecisionException("CatalogTimeout"); }
            if (!DecisionModelIdentity.TryGet(identities, settings.DecisionModel, out var dedicated))
                throw new LocalDecisionException("ModelMissing");
            if (DecisionModelIdentity.SameEndpoint(endpoint, local.Endpoint))
            {
                var shared = new[] { local.Model, knowledgeOptions.CurrentValue.EmbeddingModel };
                if (shared.Any(model => DecisionModelIdentity.TryGet(identities, model, out var identity) && identity == dedicated))
                    throw new LocalDecisionException("IncompatibleModel");
            }
            cleanupAllowed = true;
            owner = await residency.BeginOperationAsync(endpoint, settings.DecisionModel, deadline.Token).ConfigureAwait(false);
            var response = await client.DecideAsync(new(endpoint, settings.DecisionModel, source, questions.Json,
                settings.DecisionWarmTimeoutSeconds, settings.GateWaitTimeoutSeconds, settings.KeepAliveSeconds),
                settings.DecisionColdTimeoutSeconds, deadline.Token).ConfigureAwait(false);
            gateWait = response.GateWaitMs;
            inference = response.InferenceMs;
            cold = response.Cold;
            if (response.Payload.TryGetProperty("model", out var responseModel) &&
                (responseModel.ValueKind != JsonValueKind.String ||
                 !DecisionModelIdentity.TryGet(identities, responseModel.GetString()!, out var returnedIdentity) || returnedIdentity != dedicated))
                throw new LocalDecisionException("IncompatibleModel");
            var score = questions.ValidateNative(response.Payload, 50).Scores["decision"];
            var reasons = new List<string>();
            if (score.Tied) reasons.Add("TiedTopScores");
            if (score.Top < rules.MinTopProbability) reasons.Add("TopProbabilityBelowThreshold");
            if (score.Margin < rules.MinMargin) reasons.Add("MarginBelowThreshold");
            if (rules.MinConfidence > 0 && (score.Confidence is null || score.Confidence < rules.MinConfidence))
                reasons.Add("ConcentrationBelowThreshold");
            result = Result(reasons.Count == 0 ? "accepted" : "uncertain", score, reasons);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { result = Result("unavailable", null, ["OperationDeadline"]); }
        catch (LocalDecisionException e)
        {
            gateWait += e.GateWaitMs;
            inference += e.InferenceMs;
            result = Result("unavailable", null, [SanitizeProviderCode(e.Code)]);
        }
        catch (DecisionResponseException e) { result = Result("unavailable", null, [e.Code]); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or WorkflowValidationException or ArgumentException)
        { result = Result("unavailable", null, ["InvalidResponse"]); }
        finally
        {
            owner?.Dispose();
            if (cleanupAllowed)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5), timeProvider);
                try { await client.UnloadAsync(endpoint, settings.DecisionModel, cleanup.Token).ConfigureAwait(false); }
                catch (Exception e) when (e is not OutOfMemoryException)
                { logger.LogWarning("Decision cleanup failed for model {Model}; short keep-alive remains active.", settings.DecisionModel); }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        result = result with { Timing = result.Timing with { TotalMs = timer.Elapsed.TotalMilliseconds } };
        logger.LogInformation("Decision model {Model}, question hash {QuestionHash}, status {Status}, reasons {Reasons}, timing {Timing}.",
            settings.DecisionModel, hash, result.Status, string.Join(",", result.Reasons), result.Timing);
        return result;
    }

    private static string SanitizeProviderCode(string code) => code switch
    {
        "ModelMissing" or "ModelMissingOrRouteUnsupported" or "CatalogTimeout" or "GateWaitTimeout" or
        "InferenceTimeout" or "IncompatibleModel" or "ConnectionFailure" or "ProviderOverload" or
        "HttpFailure" or "RequestTooLarge" or "ResponseTooLarge" or "InvalidJson" or "EmptyResponse" => code,
        _ => "ProviderFailure",
    };
}
