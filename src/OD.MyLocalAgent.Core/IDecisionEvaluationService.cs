using System.Text.Json;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>Provides isolated native scoring and bounded generic evaluation without mail dependencies.</summary>
public interface IDecisionEvaluationService
{
    /// <summary>Captures one execution configuration without inference.</summary>
    DecisionExecutionSnapshot? Capture() => null;

    /// <summary>Evaluates a generic choice with a workflow-owned snapshot.</summary>
    Task<DecisionEvaluationResult> EvaluateAsync(DecisionExecutionSnapshot snapshot, string source, string question,
        JsonElement options, JsonElement? acceptance, CancellationToken cancellationToken) =>
        EvaluateAsync(source, question, options, acceptance, cancellationToken);

    /// <summary>Evaluates typed questions through the selected adapter, without engine overrides.</summary>
    Task<DecisionQuestionEvaluationResult> EvaluateQuestionsAsync(DecisionExecutionSnapshot snapshot, string source,
        DecisionQuestionSet questions, bool localSourceOnly, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Typed decision evaluation is unavailable.");
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
    DecisionAcceptance? Acceptance, IReadOnlyList<string> Reasons, string Provider, string Model, DecisionEvaluationTiming Timing)
{
    /// <summary>The selected engine, retained on unavailable results.</summary>
    public string Engine { get; init; } = "NativeDecision";
    /// <summary>The adapter's score capability, independent of response success.</summary>
    public DecisionCapabilities Capabilities { get; init; } = new(true);
    /// <summary>The meaning of acceptance for this adapter.</summary>
    public string AcceptanceBasis { get; init; } = "nativeThresholds";
}

/// <summary>Declares whether native scores are available.</summary>
public sealed record DecisionCapabilities(bool Scores);

/// <summary>Separately observed inference wait, load/inference and complete operation durations.</summary>
public sealed record DecisionEvaluationTiming(double GateWaitMs, double InferenceMs, double TotalMs, bool? Cold);
