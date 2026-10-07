namespace OD.MyLocalAgent.Core;

/// <summary>Defines opt-in local mail decision settings independently of normal agent generation.</summary>
public sealed record GmailDecisionOptions
{
    /// <summary>The persisted configuration section.</summary>
    public const string SectionName = "GmailDecision";
    /// <summary>Enables only the workflow decision reader.</summary>
    public bool Enabled { get; init; }
    /// <summary>The supported decision provider.</summary>
    public string DecisionProvider { get; set; } = "OllamaSystemOne";
    /// <summary>The optional local endpoint override.</summary>
    public string? DecisionEndpoint { get; set; }
    /// <summary>The installed decision model; never downloaded automatically.</summary>
    public string DecisionModel { get; set; } = "tev1:latest";
    /// <summary>The short residency lifetime between requests.</summary>
    public int KeepAliveSeconds { get; set; } = 10;
    /// <summary>Releases the dedicated decision model after each operation.</summary>
    public bool UnloadAfterBatch { get; set; } = true;
    /// <summary>The cold inference timeout.</summary>
    public int DecisionColdTimeoutSeconds { get; set; } = 120;
    /// <summary>The warm inference timeout.</summary>
    public int DecisionWarmTimeoutSeconds { get; set; } = 15;
    /// <summary>Enables second opinions for ambiguous results.</summary>
    public bool ArbitrationEnabled { get; init; } = true;
    /// <summary>The independent local arbitration model, or inherited Ollama model.</summary>
    public string? ArbitrationModel { get; init; }
    /// <summary>The arbitration inference timeout.</summary>
    public int ArbitrationTimeoutSeconds { get; init; } = 15;
    /// <summary>The technical unavailability policy.</summary>
    public string UnavailableBehavior { get; init; } = "LightweightLlmFallback";
    /// <summary>The fallback model, or inherited arbitration model.</summary>
    public string? FallbackModel { get; init; }
    /// <summary>The fallback inference timeout.</summary>
    public int FallbackTimeoutSeconds { get; init; } = 15;
    /// <summary>The complete operation deadline, including collection and waits.</summary>
    public int BatchTimeoutSeconds { get; init; } = 180;
    /// <summary>The independently bounded execution gate wait.</summary>
    public int GateWaitTimeoutSeconds { get; set; } = 30;
    /// <summary>The absolute message bound.</summary>
    public int MaxMessagesPerOperation { get; init; } = 100;
    /// <summary>The source character ceiling before token budgeting.</summary>
    public int MaxProjectionCharacters { get; init; } = 4000;
    /// <summary>The inclusive top-answer score threshold.</summary>
    public double MinTopProbabilityPercent { get; set; } = 80;
    /// <summary>The inclusive difference between the top two scores, in percentage points.</summary>
    public double MinMarginPercentagePoints { get; set; } = 20;
    /// <summary>The optional concentration check; zero disables it.</summary>
    public double MinConfidencePercent { get; set; }
    /// <summary>The inclusive certainty threshold for either side of a noul question.</summary>
    public double MinNoulCertaintyPercent { get; set; } = 80;
    /// <summary>The second-opinion budget percentage.</summary>
    public int MaxArbitrationPercent { get; init; } = 100;
    /// <summary>The absolute second-opinion cap; zero means all ambiguous messages.</summary>
    public int MaxArbitrationCalls { get; init; }
    /// <summary>The coverage percentage above which a quality warning is emitted.</summary>
    public int QualityWarningPercent { get; init; } = 20;

    /// <summary>Validates configuration locally, without loading models or probing the network.</summary>
    public static bool IsValid(GmailDecisionOptions o) =>
        DecisionOptions.IsValidNative(DecisionOptions.FromLegacy(o)) &&
        (o.ArbitrationModel is null || !string.IsNullOrWhiteSpace(o.ArbitrationModel)) &&
        (o.FallbackModel is null || !string.IsNullOrWhiteSpace(o.FallbackModel)) &&
        o.ArbitrationTimeoutSeconds is >= 1 and <= 120 && o.FallbackTimeoutSeconds is >= 1 and <= 120 &&
        o.BatchTimeoutSeconds is >= 10 and <= 3600 &&
        o.MaxMessagesPerOperation is >= 1 and <= 500 && o.MaxProjectionCharacters is >= 2048 and <= 12000 &&
        o.MaxArbitrationPercent is >= 0 and <= 100 && o.MaxArbitrationCalls is >= 0 and <= 500 &&
        o.QualityWarningPercent is >= 0 and <= 100 && o.UnavailableBehavior is "LightweightLlmFallback" or "Fail";

    /// <summary>Validates active mail limits independently from retained inactive native and arbitration settings.</summary>
    public static bool IsValidMail(GmailDecisionOptions o) =>
        o.BatchTimeoutSeconds is >= 10 and <= 3600 && o.MaxMessagesPerOperation is >= 1 and <= 500 &&
        o.MaxProjectionCharacters is >= 2048 and <= 12000 && o.QualityWarningPercent is >= 0 and <= 100;
}
