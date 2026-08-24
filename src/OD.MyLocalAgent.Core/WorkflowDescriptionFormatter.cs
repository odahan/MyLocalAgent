using System.Text;
using System.Text.Json;
using System.Globalization;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Produces the deterministic, lossless editable description of a workflow.
/// </summary>
public static class WorkflowDescriptionFormatter
{
    /// <summary>
    /// Formats normalized workflow definitions in declaration order.
    /// </summary>
    /// <param name="workflow">The workflow definitions.</param>
    /// <param name="taskGenerationOptions">The optional task overrides used for provenance.</param>
    /// <param name="generationDefaults">The optional Settings defaults used for effective values.</param>
    /// <returns>The deterministic English description.</returns>
    public static string Format(
        IReadOnlyList<WorkflowStepDefinition> workflow,
        GenerationOptions? taskGenerationOptions = null,
        AIGenerationDefaults? generationDefaults = null)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var builder = new StringBuilder();
        for (var index = 0; index < workflow.Count; index++)
        {
            var step = workflow[index];
            if (index > 0)
            {
                builder.AppendLine();
            }

            builder.Append("Step ").Append(index + 1).AppendLine(":");
            builder.Append("  Original form: ")
                .AppendLine(step.IsStringForm ? "string" : "object");
            builder.Append("  Alias: ").AppendLine(step.Step);
            builder.Append("  Explicit id: ")
                .AppendLine(step.Id is null ? "<omitted>" : step.Id);
            builder.Append("  Effective id: ").AppendLine(step.EffectiveId);
            builder.AppendLine("  Arguments:");
            if (step.Args.ValueKind == JsonValueKind.Undefined)
            {
                builder.AppendLine("    <omitted>");
            }
            else if (step.Args.ValueKind != JsonValueKind.Object)
            {
                builder.Append("    <invalid non-object args>: ")
                    .AppendLine(JsonSerializer.Serialize(step.Args));
            }
            else
            {
                foreach (var argument in step.Args.EnumerateObject())
                {
                    builder.Append("    ")
                        .Append(argument.Name)
                        .Append(": ")
                        .AppendLine(JsonSerializer.Serialize(argument.Value));
                }
            }

            builder.Append("  onError: ")
                .AppendLine(step.OnError?.ToString() ?? "<omitted>");
            builder.Append("  retryCount: ")
                .AppendLine(step.RetryCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<omitted>");
            builder.Append("  retryBackoffSeconds: ")
                .AppendLine(step.RetryBackoffSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<omitted>");
            builder.Append("  next: ").AppendLine(step.Next ?? "<following step>");
            builder.Append("  responseSchema: ")
                .AppendLine(step.ResponseSchema is { } schema ? schema.GetRawText() : "<omitted>");
            AppendGenerationOptions(
                builder,
                step,
                taskGenerationOptions,
                generationDefaults);
            if (ToolMap.Find(step.Step)?.IsControlFlowStep != true)
            {
                builder.Append("  Produced output: {{")
                    .Append(step.EffectiveId)
                    .AppendLine(".output}}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendGenerationOptions(
        StringBuilder builder,
        WorkflowStepDefinition step,
        GenerationOptions? taskOptions,
        AIGenerationDefaults? defaults)
    {
        builder.AppendLine("  Generation options:");
        var definition = ToolMap.Find(step.Step);
        if (definition?.IsAiStep != true)
        {
            builder.AppendLine("    <not applicable to this deterministic step>");
            return;
        }

        builder.Append("    configured temperature: ")
            .AppendLine(
                step.GenerationOptions?.Temperature?.ToString(
                    "G",
                    CultureInfo.InvariantCulture) ??
                "<inherit from task, then Settings>");
        builder.Append("    configured topP: ")
            .AppendLine(
                step.GenerationOptions?.TopP?.ToString(
                    "G",
                    CultureInfo.InvariantCulture) ??
                "<inherit from task, then Settings>");
        if (defaults is null)
        {
            return;
        }

        var effective = GenerationPolicyResolver.Resolve(
            defaults,
            taskOptions,
            step.GenerationOptions);
        builder.Append("    effective temperature: ")
            .Append(effective.Temperature.ToString(
                "G",
                CultureInfo.InvariantCulture))
            .Append(" (")
            .Append(effective.TemperatureSource)
            .AppendLine(")");
        builder.Append("    effective topP: ")
            .Append(effective.TopP.ToString(
                "G",
                CultureInfo.InvariantCulture))
            .Append(" (")
            .Append(effective.TopPSource)
            .AppendLine(")");
    }
}
