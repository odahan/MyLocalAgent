using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

public sealed partial class WorkflowStepEditorViewModel
{
    private readonly IDecisionEvaluationService? _decisionEvaluation;
    private readonly IOptionsMonitor<DecisionOptions>? _decisionOptions;
    private bool _loadingEvaluationFields;
    private bool _evaluationFieldsDirty;
    private int _evaluationGeneration;

    [ObservableProperty] private DecisionCriterionEditorItem? _selectedEvaluationOption;
    [ObservableProperty] private bool _overrideEvaluationTop;
    [ObservableProperty] private bool _overrideEvaluationMargin;
    [ObservableProperty] private string _evaluationTopPercent = string.Empty;
    [ObservableProperty] private string _evaluationMarginPercent = string.Empty;
    [ObservableProperty] private string _evaluationSample = string.Empty;
    [ObservableProperty] private string _evaluationMessage = string.Empty;
    [ObservableProperty] private string _evaluationSummary = string.Empty;
    [ObservableProperty] private string _evaluationTiming = string.Empty;
    [ObservableProperty] private string _evaluationReasons = string.Empty;
    [ObservableProperty] private string _evaluationFieldsError = string.Empty;
    [ObservableProperty] private bool _isTestingDecision;

    /// <summary>Gets whether the selected tool exposes the simple local choice editor.</summary>
    public bool IsDecisionEvaluationSelected => SelectedTool?.Alias == "decision.evaluate";
    /// <summary>Gets whether the general argument editor should initially be expanded.</summary>
    public bool ShowGenericArguments => !IsDecisionEvaluationSelected;
    /// <summary>Gets the canonical source argument, including its placeholder selection.</summary>
    public WorkflowStepArgumentEditorItem? EvaluationSourceArgument => Arguments.FirstOrDefault(a => a.Definition.Name == "source");
    /// <summary>Gets the canonical literal question argument.</summary>
    public WorkflowStepArgumentEditorItem? EvaluationQuestionArgument => Arguments.FirstOrDefault(a => a.Definition.Name == "question");
    /// <summary>Gets the ordered options persisted in the existing JSON contract.</summary>
    public ObservableCollection<DecisionCriterionEditorItem> EvaluationOptions { get; } = [];
    /// <summary>Gets every score returned by the last explicit test.</summary>
    public ObservableCollection<DecisionOptionScoreDisplay> EvaluationScores { get; } = [];
    /// <summary>Gets the configured minimums without implying that concentration measures correctness.</summary>
    public string EvaluationDefaultsText
    {
        get
        {
            var settings = _decisionOptions?.CurrentValue ?? new DecisionOptions();
            return FormattableString.Invariant($"Inherited minimums: top {settings.MinTopProbabilityPercent:0.##}%, margin {settings.MinMarginPercentagePoints:0.##} percentage points, concentration {settings.MinConfidencePercent:0.##}%. Overrides can only increase these minimums.");
        }
    }

    /// <summary>Gets the effective minimums for the current draft, or its validation error.</summary>
    public string EvaluationEffectiveText
    {
        get
        {
            var settings = _decisionOptions?.CurrentValue ?? new DecisionOptions();
            try
            {
                var overrides = new Dictionary<string, double>();
                if (OverrideEvaluationTop) overrides["minTopProbability"] = ParseEvaluationPercent(EvaluationTopPercent);
                if (OverrideEvaluationMargin) overrides["minMargin"] = ParseEvaluationPercent(EvaluationMarginPercent);
                var effective = DecisionEvaluateContract.Acceptance(JsonSerializer.SerializeToElement(overrides), settings);
                return FormattableString.Invariant($"Effective: top {effective.MinTopProbability * 100:0.##}% ({(OverrideEvaluationTop ? "Step" : "Settings")}), margin {effective.MinMargin * 100:0.##} pp ({(OverrideEvaluationMargin ? "Step" : "Settings")}), concentration {effective.MinConfidence * 100:0.##}% (Settings).");
            }
            catch (WorkflowValidationException exception) { return exception.Message; }
        }
    }

    private static bool IsEvaluationUiProperty(string? name) => name is not null &&
        (name.StartsWith("Evaluation", StringComparison.Ordinal) || name is nameof(IsDecisionEvaluationSelected)
            or nameof(ShowGenericArguments) or nameof(SelectedEvaluationOption) or nameof(IsTestingDecision)
            or nameof(OverrideEvaluationTop) or nameof(OverrideEvaluationMargin));

    partial void OnOverrideEvaluationTopChanged(bool value) => EvaluationFieldsChanged();
    partial void OnOverrideEvaluationMarginChanged(bool value) => EvaluationFieldsChanged();
    partial void OnEvaluationTopPercentChanged(string value) => EvaluationFieldsChanged();
    partial void OnEvaluationMarginPercentChanged(string value) => EvaluationFieldsChanged();

    private void EvaluationFieldsChanged()
    {
        if (_loadingEvaluationFields || _initializing) return;
        _evaluationFieldsDirty = true;
        HasChanges = true;
        OnPropertyChanged(nameof(EvaluationEffectiveText));
    }

    [RelayCommand]
    private void LoadEvaluationFields()
    {
        _loadingEvaluationFields = true;
        try
        {
            foreach (var option in EvaluationOptions) option.PropertyChanged -= OnEvaluationOptionChanged;
            EvaluationOptions.Clear();
            SelectedEvaluationOption = null;
            OverrideEvaluationTop = false;
            OverrideEvaluationMargin = false;
            EvaluationTopPercent = string.Empty;
            EvaluationMarginPercent = string.Empty;
            EvaluationFieldsError = string.Empty;
            _evaluationFieldsDirty = false;
            if (!IsDecisionEvaluationSelected) return;
            var options = Arguments.First(a => a.Definition.Name == "options").Parse();
            if (options.ValueKind != JsonValueKind.Object) throw new WorkflowValidationException("Options must be a JSON object.");
            foreach (var option in options.EnumerateObject())
            {
                var row = new DecisionCriterionEditorItem { Key = option.Name, Description = option.Value.GetString() ?? string.Empty };
                row.PropertyChanged += OnEvaluationOptionChanged;
                EvaluationOptions.Add(row);
            }
            var acceptance = Arguments.First(a => a.Definition.Name == "acceptance");
            if (acceptance.IsIncluded)
            {
                var json = acceptance.Parse();
                _ = DecisionEvaluateContract.Acceptance(json, new() { MinTopProbabilityPercent = 0, MinMarginPercentagePoints = 0 });
                if (json.TryGetProperty("minTopProbability", out var top))
                {
                    OverrideEvaluationTop = true;
                    EvaluationTopPercent = (top.GetDouble() * 100).ToString("G17", CultureInfo.InvariantCulture);
                }
                if (json.TryGetProperty("minMargin", out var margin))
                {
                    OverrideEvaluationMargin = true;
                    EvaluationMarginPercent = (margin.GetDouble() * 100).ToString("G17", CultureInfo.InvariantCulture);
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or WorkflowValidationException)
        {
            EvaluationFieldsError = "Correct the raw JSON before editing option fields: " + exception.Message;
        }
        finally
        {
            _loadingEvaluationFields = false;
            OnPropertyChanged(nameof(EvaluationSourceArgument));
            OnPropertyChanged(nameof(EvaluationQuestionArgument));
            OnPropertyChanged(nameof(EvaluationDefaultsText));
            OnPropertyChanged(nameof(EvaluationEffectiveText));
        }
    }

    private void OnEvaluationOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => EvaluationFieldsChanged();

    [RelayCommand]
    private void AddEvaluationOption()
    {
        if (EvaluationOptions.Count >= 24) { EvaluationMessage = "A decision supports at most 24 options."; return; }
        var index = 1;
        while (EvaluationOptions.Any(o => o.Key.Equals("option" + index, StringComparison.OrdinalIgnoreCase))) index++;
        var row = new DecisionCriterionEditorItem { Key = "option" + index, Description = "Describe when to choose this option." };
        row.PropertyChanged += OnEvaluationOptionChanged;
        EvaluationOptions.Add(row);
        SelectedEvaluationOption = row;
        EvaluationFieldsChanged();
    }

    [RelayCommand]
    private void RemoveEvaluationOption()
    {
        if (SelectedEvaluationOption is null) return;
        if (EvaluationOptions.Count <= 2) { EvaluationMessage = "A decision requires at least two options."; return; }
        SelectedEvaluationOption.PropertyChanged -= OnEvaluationOptionChanged;
        EvaluationOptions.Remove(SelectedEvaluationOption);
        SelectedEvaluationOption = null;
        EvaluationFieldsChanged();
    }

    [RelayCommand]
    private void MoveEvaluationOptionUp() => MoveEvaluationOption(-1);
    [RelayCommand]
    private void MoveEvaluationOptionDown() => MoveEvaluationOption(1);

    private void MoveEvaluationOption(int offset)
    {
        if (SelectedEvaluationOption is null) return;
        var index = EvaluationOptions.IndexOf(SelectedEvaluationOption);
        var destination = index + offset;
        if (destination < 0 || destination >= EvaluationOptions.Count) return;
        EvaluationOptions.Move(index, destination);
        EvaluationFieldsChanged();
    }

    private JsonElement EvaluationArguments(string? sampleSource = null)
    {
        var args = CreateStep().Args.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        if (_evaluationFieldsDirty)
        {
            if (!string.IsNullOrEmpty(EvaluationFieldsError)) throw new WorkflowValidationException(EvaluationFieldsError);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var option in EvaluationOptions) writer.WriteString(option.Key, option.Description);
                writer.WriteEndObject();
            }
            using var document = JsonDocument.Parse(stream.ToArray());
            args["options"] = document.RootElement.Clone();
            var acceptance = new Dictionary<string, double>();
            if (OverrideEvaluationTop) acceptance["minTopProbability"] = ParseEvaluationPercent(EvaluationTopPercent);
            if (OverrideEvaluationMargin) acceptance["minMargin"] = ParseEvaluationPercent(EvaluationMarginPercent);
            if (acceptance.Count > 0) args["acceptance"] = JsonSerializer.SerializeToElement(acceptance);
            else args.Remove("acceptance");
        }
        if (sampleSource is not null) args["source"] = JsonSerializer.SerializeToElement(sampleSource);
        var result = JsonSerializer.SerializeToElement(args);
        DecisionEvaluateContract.Validate(result);
        _ = DecisionEvaluateContract.Acceptance(result.TryGetProperty("acceptance", out var rules) ? rules : null,
            _decisionOptions?.CurrentValue ?? new DecisionOptions());
        return result;
    }

    private static double ParseEvaluationPercent(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value < 0 || value > 100)
            throw new WorkflowValidationException("Acceptance percentages must be between 0 and 100 (use a decimal point).");
        return value / 100;
    }

    private void CommitEvaluationFields()
    {
        var args = EvaluationArguments();
        if (!_evaluationFieldsDirty) return;
        _loadingEvaluationFields = true;
        try
        {
            SetDecisionArgument("options", args.GetProperty("options").GetRawText());
            var acceptance = Arguments.First(a => a.Definition.Name == "acceptance");
            acceptance.IsIncluded = args.TryGetProperty("acceptance", out var rules);
            if (acceptance.IsIncluded) acceptance.Value = rules.GetRawText();
            _evaluationFieldsDirty = false;
        }
        finally { _loadingEvaluationFields = false; }
    }

    /// <summary>Cancels a test when its editor closes or changes tools and suppresses late results.</summary>
    public void CancelDecisionTest()
    {
        _evaluationGeneration++;
        TestDecisionCommand.Cancel();
        EvaluationSummary = string.Empty;
        EvaluationTiming = string.Empty;
        EvaluationReasons = string.Empty;
        EvaluationScores.Clear();
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task TestDecisionAsync(CancellationToken cancellationToken)
    {
        var generation = _evaluationGeneration;
        EvaluationMessage = string.Empty;
        EvaluationSummary = string.Empty;
        EvaluationTiming = string.Empty;
        EvaluationReasons = string.Empty;
        EvaluationScores.Clear();
        OnPropertyChanged(nameof(EvaluationDefaultsText));
        OnPropertyChanged(nameof(EvaluationEffectiveText));
        try
        {
            if (!IsDecisionEvaluationSelected || _request is null || _decisionEvaluation is null)
                throw new WorkflowValidationException("Decision evaluation service is unavailable.");
            if (_decisionOptions?.CurrentValue.Enabled != true)
                throw new WorkflowValidationException("Enable decision evaluation in Settings before testing.");
            if (!_request.ToolGroups.Contains("decision", StringComparer.OrdinalIgnoreCase) &&
                !AuthorizationProposals.Any(p => p.Kind == "Tool group" && p.Key == "decision" && p.IsAccepted))
                throw new WorkflowValidationException("Allow the decision tool group for this task before testing (use the authorization proposal).");
            if (string.IsNullOrWhiteSpace(EvaluationSample))
                throw new WorkflowValidationException("Enter explicit sample text. Workflow placeholders are not resolved during a test.");
            var args = EvaluationArguments(EvaluationSample);
            var choices = args.GetProperty("options");
            IsTestingDecision = true;
            var result = await _decisionEvaluation.EvaluateAsync(EvaluationSample, args.GetProperty("question").GetString()!,
                choices, args.TryGetProperty("acceptance", out var rules) ? rules : null, cancellationToken);
            if (generation != _evaluationGeneration || cancellationToken.IsCancellationRequested) return;
            EvaluationSummary = $"Status: {result.Status} | Candidate: {result.Choice ?? "unavailable"} | Top: {FormatScore(result.TopProbability)} | Margin: {FormatScore(result.Margin, " pp")} | Concentration: {FormatScore(result.Confidence)}";
            EvaluationReasons = result.Reasons.Count == 0 ? "Acceptance checks passed." : "Reasons: " + string.Join(", ", result.Reasons);
            EvaluationTiming = FormattableString.Invariant($"{result.Provider} / {result.Model} | Gate: {result.Timing.GateWaitMs:0.##} ms | Inference: {result.Timing.InferenceMs:0.##} ms | Total (including cleanup): {result.Timing.TotalMs:0.##} ms | Cold: {result.Timing.Cold?.ToString() ?? "unknown"} | Applied minimums: {result.Acceptance.MinTopProbability * 100:0.##}% / {result.Acceptance.MinMargin * 100:0.##} pp / {result.Acceptance.MinConfidence * 100:0.##}% concentration");
            foreach (var option in choices.EnumerateObject())
            {
                double? score = result.Probabilities is not null && result.Probabilities.TryGetValue(option.Name, out var value) ? value : null;
                EvaluationScores.Add(new(option.Name, option.Value.GetString()!, FormatScore(score)));
            }
            EvaluationMessage = "Last explicit test snapshot. Editing fields does not rerun inference. Route on status before using the candidate.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (generation == _evaluationGeneration) EvaluationMessage = "Decision test cancelled.";
        }
        catch (Exception exception) when (exception is WorkflowValidationException or FormatException or JsonException)
        {
            if (generation == _evaluationGeneration) EvaluationMessage = exception.Message;
        }
        catch (Exception)
        {
            if (generation == _evaluationGeneration) EvaluationMessage = "Decision test failed. Check the local provider and decision settings.";
        }
        finally { IsTestingDecision = false; }
    }

    private static string FormatScore(double? value, string unit = "%") => value is null
        ? "unavailable"
        : (value.Value * 100).ToString("0.##", CultureInfo.InvariantCulture) + unit;
}

/// <summary>Displays one native option score without inventing a model explanation.</summary>
public sealed record DecisionOptionScoreDisplay(string Identifier, string Description, string Probability);
