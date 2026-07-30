using System.Text;
using System.Text.Json;

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
    /// <returns>The deterministic English description.</returns>
    public static string Format(IReadOnlyList<WorkflowStepDefinition> workflow)
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
            builder.Append("  Produced output: {{")
                .Append(step.EffectiveId)
                .AppendLine(".output}}");
        }

        return builder.ToString().TrimEnd();
    }
}
