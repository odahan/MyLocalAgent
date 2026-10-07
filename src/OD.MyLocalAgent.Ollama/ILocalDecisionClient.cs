using System.Text.Json;

namespace OD.MyLocalAgent.Ollama;

/// <summary>A sanitized provider failure with a fixed diagnostic code.</summary>
public sealed class LocalDecisionException(string code, bool transient = false, double gateWaitMs = 0, double inferenceMs = 0) : Exception($"Local classification failed ({code}).")
{
    /// <summary>The stable failure category, without provider bodies or source text.</summary>
    public string Code { get; } = code;
    /// <summary>Whether one operation-scoped retry is allowed.</summary>
    public bool Transient { get; } = transient;
    /// <summary>Gets elapsed execution-gate wait before failure, when observable.</summary>
    public double GateWaitMs { get; } = gateWaitMs;
    /// <summary>Gets combined load/inference elapsed time before failure, when observable.</summary>
    public double InferenceMs { get; } = inferenceMs;
}

/// <summary>The bounded, isolated local inference request.</summary>
public sealed record LocalDecisionRequest(
    string Endpoint, string Model, string Source, JsonElement Questions,
    int TimeoutSeconds, int GateWaitTimeoutSeconds, int KeepAliveSeconds)
{
    /// <summary>Overrides the mail-specific instruction only for an explicit isolated generic benchmark.</summary>
    public string? ClassificationInstructions { get; init; }
}

/// <summary>A provider response with separately measured wait and inference time.</summary>
public sealed record LocalDecisionResponse(JsonElement Payload, double GateWaitMs, double InferenceMs, bool Cold)
{
    /// <summary>Gets provider-reported input token usage, when present.</summary>
    public int? InputTokens { get; init; }
    /// <summary>Gets provider-reported output token usage, when present.</summary>
    public int? OutputTokens { get; init; }
    /// <summary>Gets a fixed validated finish code rather than arbitrary provider text.</summary>
    public string? FinishReason { get; init; }
}

/// <summary>Provides local SystemOne, isolated conventional classification, and lifecycle operations.</summary>
public interface ILocalDecisionClient
{
    /// <summary>Gets installed model identities once per operation.</summary>
    Task<IReadOnlyDictionary<string, string>> GetModelIdentitiesAsync(string endpoint, CancellationToken cancellationToken);
    /// <summary>Scores the configured questions with the dedicated decision endpoint.</summary>
    Task<LocalDecisionResponse> DecideAsync(LocalDecisionRequest request, int coldTimeoutSeconds, CancellationToken cancellationToken);
    /// <summary>Classifies with one non-streaming, tool-free, thinking-disabled local chat request.</summary>
    Task<LocalDecisionResponse> ClassifyAsync(LocalDecisionRequest request, JsonElement schema, CancellationToken cancellationToken);
    /// <summary>Releases only a dedicated model under bounded independent cleanup cancellation.</summary>
    Task UnloadAsync(string endpoint, string model, CancellationToken cancellationToken);
    /// <summary>Releases a verified dedicated decision model while the caller holds the embedding request boundary.</summary>
    Task ReleaseForEmbeddingAsync(string endpoint, string model, CancellationToken cancellationToken);
}
