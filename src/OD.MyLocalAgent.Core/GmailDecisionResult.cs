namespace OD.MyLocalAgent.Core;

internal sealed record GmailDecisionResult
{
    public int SchemaVersion { get; init; } = 1;
    public string? Engine { get; init; }
    public DecisionCapabilities? Capabilities { get; init; }
    public string? AcceptanceBasis { get; init; }
    public string? Provider { get; init; }
    public string Status { get; set; } = "Completed";
    public string ClassificationMode { get; init; } = "Hybrid";
    public DecisionSelection Selection { get; init; } = new("Recent", 1);
    public DateTimeOffset CollectedAt { get; init; }
    public bool CollectionPartial { get; init; }
    public Dictionary<string, string> EffectiveModels { get; init; } = [];
    public Dictionary<string, AnswerDefinition> QuestionDefinitions { get; init; } = [];
    public ReportMapping ReportMapping { get; init; } = new();
    public DecisionCounts Counts { get; set; } = new();
    public List<DecisionMailResult> Messages { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public bool ScoreDetailsOmitted { get; set; }
    public double CollectionMs { get; init; }
    public double TotalMs { get; set; }
}
internal sealed record DecisionSelection(string Kind, int? DaysBack);
internal sealed record AnswerDefinition(string Type, IReadOnlyList<string> Keys);
internal sealed record DecisionCounts
{
    public int Collected { get; init; }
    public int Processed { get; init; }
    public int Classified { get; init; }
    public int DecisionAccepted { get; init; }
    public int ArbitrationAttempted { get; init; }
    public int ArbitrationResolved { get; init; }
    public int LightweightFallbackResolved { get; init; }
    public int DirectLlmResolved { get; init; }
    public int Provisional { get; init; }
    public int Unresolved { get; init; }
}
internal sealed record DecisionMailResult
{
    public string Id { get; init; } = "";
    public string Sender { get; init; } = "";
    public string Subject { get; init; } = "";
    public DateTimeOffset ReceivedAt { get; init; }
    public string? GmailUrl { get; init; }
    public string Status { get; set; } = "Unresolved";
    public string? Category { get; set; }
    public string DecisionSource { get; set; } = "none";
    public Dictionary<string, object> Answers { get; set; } = [];
    public Dictionary<string, NativeDecisionScore> DecisionScores { get; set; } = [];
    public List<string> ArbitrationReasons { get; set; } = [];
    public bool Disagreement { get; set; }
    public bool RequiresHumanReview { get; set; } = true;
    public DecisionProjectionMetadata Projection { get; set; } = new(false, false);
    public DecisionTiming Timing { get; init; } = new();
    public bool Processed { get; set; }
    public bool ArbitrationAttempted { get; set; }
}
internal sealed record DecisionProjectionMetadata(bool CurrentContentTruncated, bool QuotedHistoryRemoved)
{
    /// <summary>Null denotes an older result that did not distinguish model-specific omissions.</summary>
    public bool? DecisionContentTruncated { get; init; }

    /// <summary>Null denotes an older result that did not distinguish model-specific omissions.</summary>
    public bool? LlmContentTruncated { get; init; }
}
internal sealed record DecisionTiming
{
    public double GateWaitMs { get; set; }
    public double DecisionMs { get; set; }
    public double ArbitrationMs { get; set; }
    public double FallbackMs { get; set; }
    public double ClassificationMs { get; set; }
    public bool ColdOrReload { get; set; }
}
