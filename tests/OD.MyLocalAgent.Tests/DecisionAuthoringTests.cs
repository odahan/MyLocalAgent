using System.Text;
using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies authoring round-trip, starter contracts and visible run warning status.</summary>
public sealed class DecisionAuthoringTests
{
    /// <summary>Named fields preserve all question types and rubric order, including Unicode descriptions.</summary>
    [Fact]
    public void NamedQuestionFieldsRoundTripAndReorderRubric()
    {
        using var questions = JsonDocument.Parse(GmailDecisionDefaults.QuestionsJson);
        var editors = questions.RootElement.EnumerateObject().Select(p => new DecisionQuestionEditorItem(p.Name, p.Value)).ToList();
        var priority = editors.Single(e => e.Name == "priority");
        priority.SelectedCriterion = priority.Criteria[2];
        priority.MoveAnswerUpCommand.Execute(null);
        priority.Criteria[1].Key.Should().Be("1");
        priority.Criteria[1].Description = "Échéance immédiate 漢😀";
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var editor in editors) editor.Write(writer);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        _ = DecisionQuestionSet.Parse(document.RootElement);
        document.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("category", "reply", "priority");
        document.RootElement.GetProperty("priority").GetProperty("criteria")[1].GetString().Should().Be("Échéance immédiate 漢😀");
        document.RootElement.GetProperty("reply").TryGetProperty("criteria", out _).Should().BeFalse();
    }

    /// <summary>The triage starter binds through the declarative editor and validates as a normal workflow.</summary>
    [Fact]
    public void EditorStarterProducesValidWorkflowOnlyStep()
    {
        var editor = new WorkflowStepEditorViewModel();
        editor.Initialize(new("[]", 0, WorkflowStepEditorMode.InsertBefore, ["gmailDecision"], [], [], [], WorkflowErrorPolicy.StopOnError, 0.2, 1));
        editor.SelectedTool = ToolCatalog.Default.Find("gmailDecision.readClassify");
        editor.LoadTriageExampleCommand.Execute(null);
        editor.IsGmailDecisionSelected.Should().BeTrue();
        editor.SupportsForEach.Should().BeFalse();
        editor.DecisionQuestions.Should().HaveCount(3);
        editor.ApplyCommand.Execute(null);
        editor.Result.Should().NotBeNull(editor.StatusText);
        var workflow = WorkflowDefinitionSerializer.Deserialize(editor.Result!.WorkflowText);
        workflow.Should().ContainSingle();
        workflow[0].Step.Should().Be("gmailDecision.readClassify");
        GmailDecisionContractValidator.Validate(workflow[0].Args);
    }

    /// <summary>Successful warning runs retain that state after catalog enumeration and incremental tail reading.</summary>
    [Fact]
    public void RunWarningsPersistAndDisplay()
    {
        var directory = Path.Combine(Path.GetTempPath(), "decision-run-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var session = RunLogSession.Start("test", 10, TimeProvider.System, directory))
            {
                session.MarkWarnings();
                session.Complete(0);
            }
            var catalog = new RunLogCatalog(directory);
            var run = catalog.GetRuns().Single();
            run.HasWarnings.Should().BeTrue();
            new RunListItem(run).Status.Should().Be("Completed with warnings");
            catalog.ReadLines(run.FilePath, 0).HasWarnings.Should().BeTrue();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>The delivered disabled sample validates using the application's exact enum spelling and Tool Map.</summary>
    [Fact]
    public void ShippedSampleValidatesWithoutInstallingOrRunningIt()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OD.MyLocalAgent.sln"))) root = root.Parent;
        root.Should().NotBeNull();
        var task = ScheduledTaskSerializer.Deserialize(File.ReadAllText(Path.Combine(root!.FullName, "Docs", "samples", "gmail-decision-watch.json")));
        task.Enabled.Should().BeFalse();
        task.Tools.Should().Equal("gmailDecision", "filesystem");
        task.Workflow[0].OnError.Should().Be(WorkflowErrorPolicy.StopOnError);
        ScheduledTaskSerializer.Deserialize(ScheduledTaskSerializer.Serialize(task)).Workflow.Should().HaveCount(3);
    }
}
