using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Parses and serializes the standalone workflow-array contract.
/// </summary>
public static class WorkflowDefinitionSerializer
{
    /// <summary>
    /// Deserializes a complete JSON workflow array without accepting surrounding content.
    /// </summary>
    /// <param name="json">The complete JSON array.</param>
    /// <returns>The normalized workflow definitions.</returns>
    public static IReadOnlyList<WorkflowStepDefinition> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new WorkflowValidationException(
                    "Workflow JSON must contain one array and no surrounding content.");
            }

            return JsonSerializer.Deserialize<WorkflowStepDefinition[]>(
                    json,
                    ScheduledTaskSerializer.Options)
                ?? throw new WorkflowValidationException(
                    "Workflow JSON must contain an array.");
        }
        catch (JsonException exception)
        {
            throw new WorkflowValidationException(
                $"Workflow JSON is invalid: {exception.Message}");
        }
    }

    /// <summary>
    /// Serializes workflow definitions as the canonical indented array.
    /// </summary>
    /// <param name="workflow">The normalized workflow definitions.</param>
    /// <returns>The canonical JSON array.</returns>
    public static string Serialize(IReadOnlyList<WorkflowStepDefinition> workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        return JsonSerializer.Serialize(workflow, ScheduledTaskSerializer.Options);
    }
}
