using Microsoft.Extensions.Configuration;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>Owns shared native inference settings and independent generic evaluation limits.</summary>
public sealed record DecisionOptions
{
    /// <summary>The canonical configuration section.</summary>
    public const string SectionName = "Decision";
    /// <summary>Enables generic workflow evaluation independently of Gmail.</summary>
    public bool Enabled { get; set; }
    /// <summary>The local native provider.</summary>
    public string DecisionProvider { get; set; } = "OllamaSystemOne";
    /// <summary>The loopback endpoint override; null inherits local Ollama.</summary>
    public string? DecisionEndpoint { get; set; }
    /// <summary>The installed dedicated model, never automatically pulled.</summary>
    public string DecisionModel { get; set; } = "tev1:latest";
    /// <summary>The bounded residency lifetime in seconds.</summary>
    public int KeepAliveSeconds { get; set; } = 10;
    /// <summary>Requires cleanup after the last operation owner releases its lease.</summary>
    public bool UnloadAfterBatch { get; set; } = true;
    /// <summary>The cold inference bound in seconds.</summary>
    public int DecisionColdTimeoutSeconds { get; set; } = 120;
    /// <summary>The warm inference bound in seconds.</summary>
    public int DecisionWarmTimeoutSeconds { get; set; } = 15;
    /// <summary>The inference gate wait bound in seconds.</summary>
    public int GateWaitTimeoutSeconds { get; set; } = 30;
    /// <summary>The complete generic operation deadline, including catalog and ownership waits.</summary>
    public int OperationTimeoutSeconds { get; set; } = 180;
    /// <summary>The conservative full-prompt budget; at most the observed 2050-token native context.</summary>
    public int MaxInputTokens { get; set; } = 2050;
    /// <summary>The inclusive top probability threshold, persisted in legacy-compatible percent units.</summary>
    public double MinTopProbabilityPercent { get; set; } = 80;
    /// <summary>The inclusive margin threshold in percentage points.</summary>
    public double MinMarginPercentagePoints { get; set; } = 20;
    /// <summary>The optional distribution concentration threshold; zero disables it.</summary>
    public double MinConfidencePercent { get; set; }
    /// <summary>The shared Boolean certainty threshold retained for existing native callers.</summary>
    public double MinNoulCertaintyPercent { get; set; } = 80;

    /// <summary>Resolves one authoritative section, preserving legacy defaults without enabling the generic tool.</summary>
    public static DecisionOptions Resolve(IConfiguration configuration)
    {
        var canonical = configuration.GetSection(SectionName);
        if (HasCanonicalSection(configuration)) return canonical.Get<DecisionOptions>() ?? new();
        return FromLegacy(configuration.GetSection(GmailDecisionOptions.SectionName).Get<GmailDecisionOptions>() ?? new());
    }

    /// <summary>Recognizes explicit empty sections as authoritative defaults, including JSON provider null markers.</summary>
    public static bool HasCanonicalSection(IConfiguration configuration) =>
        configuration.GetSection(SectionName).Exists() ||
        configuration is IConfigurationRoot root && root.Providers.Any(provider => provider.TryGet(SectionName, out _));

    /// <summary>Copies legacy native values; mail counts and mail deadlines are not generic budgets.</summary>
    public static DecisionOptions FromLegacy(GmailDecisionOptions value) => new()
    {
        DecisionProvider = value.DecisionProvider, DecisionEndpoint = value.DecisionEndpoint,
        DecisionModel = value.DecisionModel, KeepAliveSeconds = value.KeepAliveSeconds,
        UnloadAfterBatch = value.UnloadAfterBatch, DecisionColdTimeoutSeconds = value.DecisionColdTimeoutSeconds,
        DecisionWarmTimeoutSeconds = value.DecisionWarmTimeoutSeconds, GateWaitTimeoutSeconds = value.GateWaitTimeoutSeconds,
        MinTopProbabilityPercent = value.MinTopProbabilityPercent, MinMarginPercentagePoints = value.MinMarginPercentagePoints,
        MinConfidencePercent = value.MinConfidencePercent, MinNoulCertaintyPercent = value.MinNoulCertaintyPercent,
    };

    /// <summary>Projects canonical native settings into the compatible mail contract without changing mail policy.</summary>
    public GmailDecisionOptions ApplyTo(GmailDecisionOptions mail) => mail with
    {
        DecisionProvider = DecisionProvider, DecisionEndpoint = DecisionEndpoint, DecisionModel = DecisionModel,
        KeepAliveSeconds = KeepAliveSeconds, UnloadAfterBatch = UnloadAfterBatch,
        DecisionColdTimeoutSeconds = DecisionColdTimeoutSeconds, DecisionWarmTimeoutSeconds = DecisionWarmTimeoutSeconds,
        GateWaitTimeoutSeconds = GateWaitTimeoutSeconds, MinTopProbabilityPercent = MinTopProbabilityPercent,
        MinMarginPercentagePoints = MinMarginPercentagePoints, MinConfidencePercent = MinConfidencePercent,
        MinNoulCertaintyPercent = MinNoulCertaintyPercent,
    };

    /// <summary>Validates bounds without network access.</summary>
    public static bool IsValid(DecisionOptions value) => IsValidNative(value) &&
        value.OperationTimeoutSeconds is >= 1 and <= 3600 && value.MaxInputTokens is >= 512 and <= 2050;

    internal static bool IsValidNative(DecisionOptions value) =>
        value.DecisionProvider == "OllamaSystemOne" && !string.IsNullOrWhiteSpace(value.DecisionModel) &&
        (value.DecisionEndpoint is null || LocalDecisionEndpoint.IsValid(value.DecisionEndpoint)) &&
        value.KeepAliveSeconds is >= 0 and <= 60 && value.UnloadAfterBatch &&
        value.DecisionColdTimeoutSeconds is >= 1 and <= 300 && value.DecisionWarmTimeoutSeconds is >= 1 and <= 120 &&
        value.GateWaitTimeoutSeconds is >= 1 and <= 300 &&
        new[] { value.MinTopProbabilityPercent, value.MinMarginPercentagePoints, value.MinConfidencePercent, value.MinNoulCertaintyPercent }
            .All(number => double.IsFinite(number) && number is >= 0 and <= 100) && value.MinNoulCertaintyPercent >= 50;
}
