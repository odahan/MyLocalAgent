using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Runner;

/// <summary>Builds an in-memory recovery workflow using durable run data and the task's existing authorization.</summary>
public static class EditorialRecoveryTaskBuilder
{
    /// <summary>Resumes drafting or export without discovery, selection, or task-store changes.</summary>
    public static ScheduledTask Build(ScheduledTask task, EditorialRecoveryState state, TimeZoneInfo timeZone)
    {
        var reservation = state.Reservation;
        if (reservation.Status is not ("Reserved" or "ReadyToExport" or "Exported"))
            throw new InvalidOperationException($"Cannot recover editorial status '{reservation.Status}'.");

        var date = TimeZoneInfo.ConvertTime(state.CreatedAtUtc, timeZone);
        string Resolve(string value) => Regex.Replace(
            value.Replace("{{workflow.runId}}", reservation.RunId, StringComparison.Ordinal),
            @"\{\{Today\[([^\]]+)\]\}\}", match => date.ToString(match.Groups[1].Value, CultureInfo.InvariantCulture));
        WorkflowStepDefinition Step(string id) => task.Workflow.Single(step => step.EffectiveId == id);
        WorkflowStepDefinition WithArgs(WorkflowStepDefinition step, Dictionary<string, JsonElement> args) =>
            step with { Args = JsonSerializer.SerializeToElement(args) };
        Dictionary<string, JsonElement> Args(WorkflowStepDefinition step) => step.Args.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.String
                ? JsonSerializer.SerializeToElement(Resolve(property.Value.GetString()!)) : property.Value.Clone());

        var export = Step("exportEditorialReport");
        var exportArgs = Args(export);
        var reportPath = state.ReportPath ?? exportArgs["path"].GetString()!;
        var confirmation = Step("confirmEditorialExport");
        var confirmationArgs = Args(confirmation);
        confirmationArgs["reportPath"] = JsonSerializer.SerializeToElement(reportPath);
        var steps = new List<WorkflowStepDefinition>();

        if (reservation.Status == "Reserved")
        {
            if (reservation.Approved.Count == 0)
                throw new InvalidOperationException("Draft recovery requires at least one approved subject.");
            var draft = Step("draftApprovedSubjects");
            var draftArgs = Args(draft);
            var reservationJson = JsonSerializer.Serialize(new
            {
                runId = reservation.RunId, approved = reservation.Approved,
                approvedCount = reservation.Approved.Count, rejected = reservation.Rejected,
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            draftArgs["prompt"] = JsonSerializer.SerializeToElement(
                draftArgs["prompt"].GetString()!.Replace("{{reserveEditorialSubjects.output}}", reservationJson, StringComparison.Ordinal));
            steps.Add(WithArgs(draft, draftArgs));
            var prepare = Step("prepareEditorialReport");
            steps.Add(WithArgs(prepare, Args(prepare)));
            steps.Add(WithArgs(export, exportArgs));
        }
        else if (reservation.Status == "ReadyToExport" && !File.Exists(reportPath))
        {
            if (string.IsNullOrWhiteSpace(state.Markdown))
                throw new InvalidOperationException("The prepared report is missing from the database.");
            exportArgs["content"] = JsonSerializer.SerializeToElement(state.Markdown);
            steps.Add(WithArgs(export, exportArgs));
        }
        steps.Add(WithArgs(confirmation, confirmationArgs));
        var recovery = task with { Workflow = steps };
        ScheduledTaskValidator.Validate(recovery);
        return recovery;
    }
}
