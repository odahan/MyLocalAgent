using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

public sealed partial class WorkflowStepEditorViewModel
{
    private bool _decisionQuestionsDirty;
    /// <summary>Gets whether the current step exposes local Gmail questions.</summary>
    public bool IsGmailDecisionSelected => SelectedTool?.Alias == "gmailDecision.readClassify";
    /// <summary>Gets named question editors; the JSON argument remains the persisted format.</summary>
    public ObservableCollection<DecisionQuestionEditorItem> DecisionQuestions { get; } = [];

    [RelayCommand]
    private void LoadTriageExample()
    {
        SetDecisionArgument("questions", GmailDecisionDefaults.QuestionsJson);
        SetDecisionArgument("reportMapping", GmailDecisionDefaults.ReportMappingJson);
        SetDecisionArgument("acceptance", GmailDecisionDefaults.AcceptanceJson);
        OverrideErrorPolicy = true;
        OnError = WorkflowErrorPolicy.StopOnError;
        LoadDecisionQuestionFields();
    }

    [RelayCommand]
    private void LoadDecisionQuestionFields()
    {
        DecisionQuestions.Clear();
        var argument = Arguments.FirstOrDefault(a => a.Definition.Name == "questions");
        if (!IsGmailDecisionSelected || argument is null || string.IsNullOrWhiteSpace(argument.Value)) return;
        try
        {
            using var document = JsonDocument.Parse(argument.Value);
            _ = DecisionQuestionSet.Parse(document.RootElement);
            foreach (var p in document.RootElement.EnumerateObject()) AddQuestionEditor(new(p.Name, p.Value));
            _decisionQuestionsDirty = false;
        }
        catch (Exception e) when (e is JsonException or WorkflowValidationException)
        { StatusText = "Correct the questions JSON before opening named fields: " + e.Message; }
    }

    [RelayCommand]
    private void AddDecisionQuestion()
    {
        using var json = JsonDocument.Parse("{\"type\":\"noul\",\"instructions\":\"Does the recipient need to act?\"}");
        var index = 1;
        while (DecisionQuestions.Any(q => q.Name == "question" + index)) index++;
        AddQuestionEditor(new("question" + index, json.RootElement));
        _decisionQuestionsDirty = true;
        HasChanges = true;
    }

    [RelayCommand]
    private void RemoveDecisionQuestion(DecisionQuestionEditorItem? question)
    {
        if (question is not null) DecisionQuestions.Remove(question);
        _decisionQuestionsDirty = true;
        HasChanges = true;
    }

    [RelayCommand]
    private void ApplyDecisionQuestionFields()
    {
        try { CommitDecisionQuestionFields(); StatusText = "Question fields validated and applied to this step draft."; }
        catch (WorkflowValidationException e) { StatusText = e.Message; }
    }

    private void AddQuestionEditor(DecisionQuestionEditorItem item)
    {
        item.PropertyChanged += (_, _) => { _decisionQuestionsDirty = true; HasChanges = true; };
        DecisionQuestions.Add(item);
    }

    private void CommitDecisionQuestionFields()
    {
        if (!IsGmailDecisionSelected || !_decisionQuestionsDirty) return;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var question in DecisionQuestions) question.Write(writer);
            writer.WriteEndObject();
        }
        var json = Encoding.UTF8.GetString(stream.ToArray());
        using var parsed = JsonDocument.Parse(json);
        _ = DecisionQuestionSet.Parse(parsed.RootElement);
        SetDecisionArgument("questions", json);
        _decisionQuestionsDirty = false;
    }

    private void SetDecisionArgument(string name, string value)
    {
        var item = Arguments.FirstOrDefault(a => a.Definition.Name == name);
        if (item is null) return;
        item.IsIncluded = true;
        item.Value = value;
    }
}
