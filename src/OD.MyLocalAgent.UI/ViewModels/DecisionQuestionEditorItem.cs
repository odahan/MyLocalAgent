using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>Edits one named decision question and its ordered answer descriptions without a global profile store.</summary>
public sealed partial class DecisionQuestionEditorItem : ObservableObject
{
    [ObservableProperty] private string _name = "question";
    [ObservableProperty] private string _type = "choice";
    [ObservableProperty] private string _instructions = "Classify this email.";
    [ObservableProperty] private DecisionCriterionEditorItem? _selectedCriterion;
    private bool _loading;

    /// <summary>Gets supported question types.</summary>
    public IReadOnlyList<string> Types { get; } = ["choice", "noul", "score"];
    /// <summary>Gets the ordered answer/level descriptions.</summary>
    public ObservableCollection<DecisionCriterionEditorItem> Criteria { get; } = [];
    /// <summary>Gets whether criterion keys are editable.</summary>
    public bool AreKeysEditable => Type == "choice";

    /// <summary>Creates a question from an authored JSON entry, preserving criterion order.</summary>
    public DecisionQuestionEditorItem(string name, JsonElement json)
    {
        _loading = true;
        Name = name;
        Type = json.GetProperty("type").GetString()!;
        Instructions = json.GetProperty("instructions").GetString()!;
        if (json.TryGetProperty("criteria", out var criteria))
        {
            if (criteria.ValueKind == JsonValueKind.Array)
                foreach (var item in criteria.EnumerateArray()) AddCriterion(Criteria.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), item.GetString()!);
            else foreach (var item in criteria.EnumerateObject()) AddCriterion(item.Name, item.Value.GetString()!);
        }
        _loading = false;
    }

    partial void OnTypeChanged(string value)
    {
        OnPropertyChanged(nameof(AreKeysEditable));
        if (_loading) return;
        Criteria.Clear();
        if (value == "noul") { AddCriterion("true", "Yes"); AddCriterion("false", "No"); }
        else { AddCriterion(value == "score" ? "0" : "first", "First answer"); AddCriterion(value == "score" ? "1" : "second", "Second answer"); }
    }

    [RelayCommand]
    private void AddAnswer()
    {
        if (Type == "noul")
        {
            if (Criteria.Count == 0) { AddCriterion("true", "Yes"); AddCriterion("false", "No"); }
        }
        else AddCriterion(Type == "score" ? Criteria.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "answer" + (Criteria.Count + 1), "Answer description");
    }

    [RelayCommand]
    private void RemoveAnswer()
    {
        if (Type == "noul") Criteria.Clear();
        else if (SelectedCriterion is not null) Criteria.Remove(SelectedCriterion);
        Renumber();
    }

    [RelayCommand]
    private void MoveAnswerUp() => Move(-1);
    [RelayCommand]
    private void MoveAnswerDown() => Move(1);
    private void Move(int offset)
    {
        if (SelectedCriterion is null || Type == "noul") return;
        var index = Criteria.IndexOf(SelectedCriterion);
        var target = index + offset;
        if (target >= 0 && target < Criteria.Count) Criteria.Move(index, target);
        Renumber();
    }

    private void AddCriterion(string key, string description)
    {
        var criterion = new DecisionCriterionEditorItem { Key = key, Description = description };
        criterion.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Criteria));
        Criteria.Add(criterion);
        OnPropertyChanged(nameof(Criteria));
    }
    private void Renumber()
    {
        if (Type == "score")
            for (var i = 0; i < Criteria.Count; i++) Criteria[i].Key = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(Criteria));
    }

    /// <summary>Writes a lossless typed question into the ordered authored object.</summary>
    public void Write(Utf8JsonWriter writer)
    {
        writer.WritePropertyName(Name);
        writer.WriteStartObject();
        writer.WriteString("type", Type);
        writer.WriteString("instructions", Instructions);
        if (Type != "noul" || Criteria.Count != 0)
        {
            writer.WritePropertyName("criteria");
            if (Type == "score")
            {
                writer.WriteStartArray();
                foreach (var criterion in Criteria) writer.WriteStringValue(criterion.Description);
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteStartObject();
                foreach (var criterion in Criteria) writer.WriteString(criterion.Key, criterion.Description);
                writer.WriteEndObject();
            }
        }
        writer.WriteEndObject();
    }
}

/// <summary>Edits a choice key or rubric level description in the question's own draft.</summary>
public sealed partial class DecisionCriterionEditorItem : ObservableObject
{
    [ObservableProperty] private string _key = "answer";
    [ObservableProperty] private string _description = "Answer description";
}
