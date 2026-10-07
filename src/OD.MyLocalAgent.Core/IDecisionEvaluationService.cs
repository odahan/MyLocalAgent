using System.Text.Json;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>Provides isolated native scoring and bounded generic evaluation without mail dependencies.</summary>
public interface IDecisionEvaluationService
{
    /// <summary>Scores any existing native question set and validates its answer types and distributions.</summary>
    Task<LocalDecisionResponse> ScoreAsync(LocalDecisionRequest request, int coldTimeoutSeconds,
        DecisionQuestionSet questions, CancellationToken cancellationToken);

    /// <summary>Evaluates one explicit choice question using inherited configuration and strict acceptance.</summary>
    Task<DecisionEvaluationResult> EvaluateAsync(string source, string question, JsonElement options,
        JsonElement? acceptance, CancellationToken cancellationToken);
}

/// <summary>Versioned generic output; unavailable results contain null scores rather than fabricated choices.</summary>
public sealed record DecisionEvaluationResult(int SchemaVersion, string Status, string? Choice,
    IReadOnlyDictionary<string, double>? Probabilities, double? TopProbability, double? Margin, double? Confidence,
    DecisionAcceptance Acceptance, IReadOnlyList<string> Reasons, string Provider, string Model, DecisionEvaluationTiming Timing);

/// <summary>Separately observed inference wait, load/inference and complete operation durations.</summary>
public sealed record DecisionEvaluationTiming(double GateWaitMs, double InferenceMs, double TotalMs, bool? Cold);
