using System.Text.Json;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>Owns immutable effective configuration for one workflow, mail batch, or explicit editor test.</summary>
public sealed class DecisionExecutionSnapshot
{
    internal DecisionExecutionSnapshot(DecisionOptions settings, OllamaOptions local, string embeddingModel,
        LlmClientSnapshot? central, EffectiveGenerationPolicy? policy, string? failure)
    {
        Settings = settings with { };
        Local = local with { };
        EmbeddingModel = embeddingModel;
        Central = central;
        Policy = policy;
        Failure = failure;
    }

    internal DecisionOptions Settings { get; }
    internal OllamaOptions Local { get; }
    internal string EmbeddingModel { get; }
    internal LlmClientSnapshot? Central { get; }
    internal EffectiveGenerationPolicy? Policy { get; }
    internal string? Failure { get; }
    /// <summary>The selected engine name.</summary>
    public string Engine => Settings.Engine;
    /// <summary>The resolved provider identity.</summary>
    public string Provider => Engine == "NativeDecision" ? Settings.DecisionProvider : Central?.Provider ?? "unavailable";
    /// <summary>The resolved model identity.</summary>
    public string Model => Engine == "NativeDecision" ? Settings.DecisionModel : Central?.Model ?? "unavailable";
}

/// <summary>Returns validated typed answers without inventing a native distribution for central choices.</summary>
public sealed record DecisionQuestionEvaluationResult(string Status, JsonElement? Answers, JsonElement? NativePayload,
    IReadOnlyList<string> Reasons, DecisionEvaluationTiming Timing);
