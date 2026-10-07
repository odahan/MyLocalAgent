using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.UI.ViewModels;

public sealed partial class SettingsViewModel
{
    private static bool IsNativeDecisionSetting(string name) => name.StartsWith("Decision", StringComparison.Ordinal) &&
        name is not (nameof(DecisionModelTestStatus) or nameof(DecisionSettingsValidationText));
    private readonly IOptionsMonitor<GmailDecisionOptions>? _gmailDecisionOptions;
    private readonly IOptionsMonitor<DecisionOptions>? _decisionOptions;
    [ObservableProperty] private bool _decisionEnabled;
    [ObservableProperty] private string _decisionEngine = "NativeDecision";
    [ObservableProperty] private int _decisionCentralMaxOutputTokens = 512;
    [ObservableProperty] private int _decisionCentralTimeoutSeconds = 180;
    [ObservableProperty] private int _decisionCentralGateWaitSeconds = 30;
    /// <summary>Gets the global engine choices.</summary>
    public IReadOnlyList<DecisionEngineChoice> DecisionEngines { get; } =
        [new("CentralLlm", "Use application LLM"), new("NativeDecision", "Use a decision model")];
    /// <summary>Gets whether dedicated native controls apply to the selected engine.</summary>
    public bool IsNativeDecisionEngine => DecisionEngine == "NativeDecision";
    /// <summary>Gets the English product label for the selected engine.</summary>
    public string DecisionEngineDescription => IsNativeDecisionEngine ? "Use a decision model" : "Use application LLM (central provider and model)";
    partial void OnDecisionEngineChanged(string value)
    {
        OnPropertyChanged(nameof(IsNativeDecisionEngine));
        OnPropertyChanged(nameof(DecisionEngineDescription));
    }
    [ObservableProperty] private int _decisionOperationTimeoutSeconds = 180;
    [ObservableProperty] private int _decisionMaxInputTokens = 2050;
    private readonly ILocalDecisionClient? _localDecisionClient;
    private readonly DecisionModelResidencyCoordinator? _decisionResidency;
    [ObservableProperty] private string _decisionModelTestStatus = "Scores are model scores, not accuracy guarantees. Saving does not load or probe a model.";
    [ObservableProperty] private bool _isTestingDecisionModel;
    [ObservableProperty] private string _decisionSettingsValidationText = "Local decision settings are valid.";
    /// <summary>Gets installed local decision and conventional models from explicit refresh.</summary>
    public ObservableCollection<string> DecisionModels { get; } = [];
    /// <summary>Gets the supported local provider names.</summary>
    public IReadOnlyList<string> DecisionProviders { get; } = ["OllamaSystemOne"];
    /// <summary>Gets technical decision-provider unavailability policies.</summary>
    public IReadOnlyList<string> DecisionUnavailableBehaviors { get; } = ["LightweightLlmFallback", "Fail"];
    [ObservableProperty] private bool _gmailDecisionEnabled;
    [ObservableProperty] private string _decisionProvider = "OllamaSystemOne";
    [ObservableProperty] private string? _decisionEndpoint;
    [ObservableProperty] private string _decisionModel = "tev1:latest";
    [ObservableProperty] private int _decisionKeepAliveSeconds = 10;
    [ObservableProperty] private bool _decisionUnloadAfterBatch = true;
    [ObservableProperty] private int _decisionColdTimeoutSeconds = 120;
    [ObservableProperty] private int _decisionWarmTimeoutSeconds = 15;
    [ObservableProperty] private bool _gmailDecisionArbitrationEnabled = true;
    [ObservableProperty] private string? _gmailDecisionArbitrationModel;
    [ObservableProperty] private int _gmailDecisionArbitrationTimeoutSeconds = 15;
    [ObservableProperty] private string _gmailDecisionUnavailableBehavior = "LightweightLlmFallback";
    [ObservableProperty] private string? _gmailDecisionFallbackModel;
    [ObservableProperty] private int _gmailDecisionFallbackTimeoutSeconds = 15;
    [ObservableProperty] private int _gmailDecisionBatchTimeoutSeconds = 180;
    [ObservableProperty] private int _decisionGateWaitTimeoutSeconds = 30;
    [ObservableProperty] private int _gmailDecisionMaxMessagesPerOperation = 100;
    [ObservableProperty] private int _gmailDecisionMaxProjectionCharacters = 4000;
    [ObservableProperty] private double _decisionMinTopProbabilityPercent = 80;
    [ObservableProperty] private double _decisionMinMarginPercentagePoints = 20;
    [ObservableProperty] private double _decisionMinConfidencePercent;
    [ObservableProperty] private double _decisionMinNoulCertaintyPercent = 80;
    [ObservableProperty] private int _gmailDecisionMaxArbitrationPercent = 100;
    [ObservableProperty] private int _gmailDecisionMaxArbitrationCalls;
    [ObservableProperty] private int _gmailDecisionQualityWarningPercent = 20;

    private void LoadGmailDecision(GmailDecisionOptions value)
    {
        GmailDecisionEnabled = value.Enabled;
        DecisionProvider = value.DecisionProvider;
        DecisionEndpoint = value.DecisionEndpoint;
        DecisionModel = value.DecisionModel;
        DecisionKeepAliveSeconds = value.KeepAliveSeconds;
        DecisionUnloadAfterBatch = value.UnloadAfterBatch;
        DecisionColdTimeoutSeconds = value.DecisionColdTimeoutSeconds;
        DecisionWarmTimeoutSeconds = value.DecisionWarmTimeoutSeconds;
        GmailDecisionArbitrationEnabled = value.ArbitrationEnabled;
        GmailDecisionArbitrationModel = value.ArbitrationModel;
        GmailDecisionArbitrationTimeoutSeconds = value.ArbitrationTimeoutSeconds;
        GmailDecisionUnavailableBehavior = value.UnavailableBehavior;
        GmailDecisionFallbackModel = value.FallbackModel;
        GmailDecisionFallbackTimeoutSeconds = value.FallbackTimeoutSeconds;
        GmailDecisionBatchTimeoutSeconds = value.BatchTimeoutSeconds;
        DecisionGateWaitTimeoutSeconds = value.GateWaitTimeoutSeconds;
        GmailDecisionMaxMessagesPerOperation = value.MaxMessagesPerOperation;
        GmailDecisionMaxProjectionCharacters = value.MaxProjectionCharacters;
        DecisionMinTopProbabilityPercent = value.MinTopProbabilityPercent;
        DecisionMinMarginPercentagePoints = value.MinMarginPercentagePoints;
        DecisionMinConfidencePercent = value.MinConfidencePercent;
        DecisionMinNoulCertaintyPercent = value.MinNoulCertaintyPercent;
        GmailDecisionMaxArbitrationPercent = value.MaxArbitrationPercent;
        GmailDecisionMaxArbitrationCalls = value.MaxArbitrationCalls;
        GmailDecisionQualityWarningPercent = value.QualityWarningPercent;
    }

    private GmailDecisionOptions GetGmailDecisionOptions() => new()
    {
        Enabled = GmailDecisionEnabled,
        DecisionProvider = DecisionProvider,
        DecisionEndpoint = string.IsNullOrWhiteSpace(DecisionEndpoint) ? null : DecisionEndpoint.Trim(),
        DecisionModel = DecisionModel,
        KeepAliveSeconds = DecisionKeepAliveSeconds,
        UnloadAfterBatch = DecisionUnloadAfterBatch,
        DecisionColdTimeoutSeconds = DecisionColdTimeoutSeconds,
        DecisionWarmTimeoutSeconds = DecisionWarmTimeoutSeconds,
        ArbitrationEnabled = GmailDecisionArbitrationEnabled,
        ArbitrationModel = string.IsNullOrWhiteSpace(GmailDecisionArbitrationModel) ? null : GmailDecisionArbitrationModel.Trim(),
        ArbitrationTimeoutSeconds = GmailDecisionArbitrationTimeoutSeconds,
        UnavailableBehavior = GmailDecisionUnavailableBehavior,
        FallbackModel = string.IsNullOrWhiteSpace(GmailDecisionFallbackModel) ? null : GmailDecisionFallbackModel.Trim(),
        FallbackTimeoutSeconds = GmailDecisionFallbackTimeoutSeconds,
        BatchTimeoutSeconds = GmailDecisionBatchTimeoutSeconds,
        GateWaitTimeoutSeconds = DecisionGateWaitTimeoutSeconds,
        MaxMessagesPerOperation = GmailDecisionMaxMessagesPerOperation,
        MaxProjectionCharacters = GmailDecisionMaxProjectionCharacters,
        MinTopProbabilityPercent = DecisionMinTopProbabilityPercent,
        MinMarginPercentagePoints = DecisionMinMarginPercentagePoints,
        MinConfidencePercent = DecisionMinConfidencePercent,
        MinNoulCertaintyPercent = DecisionMinNoulCertaintyPercent,
        MaxArbitrationPercent = GmailDecisionMaxArbitrationPercent,
        MaxArbitrationCalls = GmailDecisionMaxArbitrationCalls,
        QualityWarningPercent = GmailDecisionQualityWarningPercent,
    };

    private DecisionOptions GetDecisionOptions() => DecisionOptions.FromLegacy(GetGmailDecisionOptions()) with
    {
        Enabled = DecisionEnabled,
        Engine = DecisionEngine,
        CentralMaxOutputTokens = DecisionCentralMaxOutputTokens,
        CentralTimeoutSeconds = DecisionCentralTimeoutSeconds,
        CentralGateWaitSeconds = DecisionCentralGateWaitSeconds,
        OperationTimeoutSeconds = DecisionOperationTimeoutSeconds,
        MaxInputTokens = DecisionMaxInputTokens,
    };

    [RelayCommand]
    private async Task RefreshDecisionModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = string.IsNullOrWhiteSpace(DecisionEndpoint) ? OllamaEndpoint : DecisionEndpoint.Trim();
            if (!LocalDecisionEndpoint.IsValid(endpoint)) throw new InvalidOperationException("Use a loopback HTTP(S) endpoint.");
            var models = await _ollamaModelCatalog.ListInstalledModelsAsync(endpoint, 15, cancellationToken).ConfigureAwait(true);
            DecisionModels.Clear();
            foreach (var model in models) DecisionModels.Add(model);
            DecisionModelTestStatus = $"{models.Count} installed local models. No model was loaded.";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { DecisionModelTestStatus = e is OperationCanceledException ? "Model refresh canceled." : "Unable to refresh the local model catalog."; }
    }

    [RelayCommand]
    private async Task TestDecisionModelAsync(CancellationToken cancellationToken)
    {
        if (!IsNativeDecisionEngine) { DecisionModelTestStatus = "Use Test decision in the workflow editor to test the selected application LLM."; return; }
        if (_localDecisionClient is null) { DecisionModelTestStatus = "Local decision client is unavailable."; return; }
        var settings = GetGmailDecisionOptions();
        if (!DecisionOptions.IsValid(GetDecisionOptions())) { DecisionModelTestStatus = "Check local decision settings and limits."; return; }
        var endpoint = settings.DecisionEndpoint ?? OllamaEndpoint;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(DecisionOperationTimeoutSeconds));
        IsTestingDecisionModel = true;
        IDisposable? owner = null;
        var safeCleanup = false;
        try
        {
            var identities = await _localDecisionClient.GetModelIdentitiesAsync(endpoint, deadline.Token).ConfigureAwait(true);
            string? Identity(string name) => identities.GetValueOrDefault(name.Contains(':') ? name : name + ":latest");
            var identity = Identity(settings.DecisionModel) ?? throw new InvalidOperationException("Decision model is not installed.");
            if (GmailDecisionTool.SameEndpoint(endpoint, OllamaEndpoint) &&
                new[] { OllamaModel, settings.ArbitrationModel ?? OllamaModel, settings.FallbackModel ?? settings.ArbitrationModel ?? OllamaModel, KnowledgeEmbeddingModel }
                .Any(model => Identity(model) == identity))
                throw new InvalidOperationException("Decision model must have a different installed identity from chat and embedding models.");
            safeCleanup = true;
            if (_decisionResidency is not null)
                owner = await _decisionResidency.BeginOperationAsync(endpoint, settings.DecisionModel, deadline.Token).ConfigureAwait(true);
            var questions = JsonSerializer.SerializeToElement(new { greeting = new { type = "noul", instructions = "Does the text contain a greeting?" } });
            var request = new LocalDecisionRequest(endpoint, settings.DecisionModel, "Hello, have a good day.", questions,
                settings.DecisionWarmTimeoutSeconds, settings.GateWaitTimeoutSeconds, settings.KeepAliveSeconds);
            var first = await _localDecisionClient.DecideAsync(request, settings.DecisionColdTimeoutSeconds, deadline.Token).ConfigureAwait(true);
            var second = await _localDecisionClient.DecideAsync(request, settings.DecisionColdTimeoutSeconds, deadline.Token).ConfigureAwait(true);
            DecisionQuestionSet.Parse(questions).ValidateResponse(second.Payload);
            DecisionModelTestStatus = $"Available: {settings.DecisionModel}; identity {identity[..Math.Min(12, identity.Length)]}; first load + inference {first.InferenceMs:0} ms ({(first.Cold ? "cold" : "resident")}); second {second.InferenceMs:0} ms ({(second.Cold ? "cold/reload" : "warm")}). Typed answer: {second.Payload.GetProperty("answers").GetProperty("greeting").GetRawText()}";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { DecisionModelTestStatus = e is OperationCanceledException ? "Decision test canceled or deadline reached." : "Decision test failed: " + e.Message; }
        finally
        {
            owner?.Dispose();
            if (safeCleanup)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await _localDecisionClient.UnloadAsync(endpoint, settings.DecisionModel, cleanup.Token).ConfigureAwait(true); }
                catch (Exception e) when (e is not OutOfMemoryException) { DecisionModelTestStatus += " Cleanup failed; short keep-alive remains active."; }
            }
            IsTestingDecisionModel = false;
        }
    }
}

/// <summary>Displays an English product label while persisting the stable engine identifier.</summary>
public sealed record DecisionEngineChoice(string Engine, string Label);
