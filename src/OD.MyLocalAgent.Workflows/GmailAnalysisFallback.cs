using System.Text.Json;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Preserves failed Gmail analyses as deterministic review records without exposing protected content.
/// </summary>
internal static class GmailAnalysisFallback
{
    /// <summary>Identifies AI iterations over a Gmail read step using workflow metadata.</summary>
    public static bool AppliesTo(WorkflowStep step, ScheduledTask task)
    {
        if (step is not PromptStep)
        {
            return false;
        }

        WorkflowStepDefinition? previous = null;
        foreach (var definition in task.Workflow)
        {
            if (string.Equals(definition.Id, step.Id, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (definition.Step is "gmail.readToday" or "gmail.readUnread" or "gmail.readRecent" &&
                string.Equals(step.ForEach, "{{" + definition.Id + ".output.messages}}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            previous = definition;
        }

        return previous?.Step is "gmail.readToday" or "gmail.readUnread" or "gmail.readRecent" &&
            string.Equals(step.ForEach, "{{previous.output.messages}}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Creates a report-compatible record from safe message metadata only.</summary>
    public static string Create(JsonElement message) => JsonSerializer.Serialize(new
    {
        id = ReadString(message, "id"),
        sender = ReadString(message, "sender", "Non disponible — consulter Gmail"),
        subject = ReadString(message, "subject", "Non disponible — consulter Gmail"),
        receivedAt = ReadString(message, "receivedAt"),
        category = "A vérifier",
        reason = "Échec d'analyse IA",
        action = "Vérifier le message original dans Gmail",
        deadline = "Non précisée",
    });

    /// <summary>Reads optional metadata without decoding or copying protected message bodies.</summary>
    private static string ReadString(JsonElement message, string name, string fallback = "") =>
        message.ValueKind == JsonValueKind.Object &&
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
}
