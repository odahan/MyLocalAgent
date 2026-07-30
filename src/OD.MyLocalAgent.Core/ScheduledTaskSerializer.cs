using System.Text.Json;
using System.Text.Json.Serialization;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides the canonical scheduled-task JSON representation.
/// </summary>
public static class ScheduledTaskSerializer
{
    /// <summary>
    /// Gets the shared serializer options.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// Deserializes and validates a scheduled task.
    /// </summary>
    /// <param name="json">The task JSON.</param>
    /// <returns>The validated task.</returns>
    public static ScheduledTask Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            var task = JsonSerializer.Deserialize<ScheduledTask>(json, Options)
                ?? throw new TaskValidationException("The task JSON did not contain an object.");
            ScheduledTaskValidator.Validate(task);
            return task;
        }
        catch (JsonException exception)
        {
            throw new TaskValidationException($"The task JSON is invalid: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Validates and serializes a scheduled task.
    /// </summary>
    /// <param name="task">The task to serialize.</param>
    /// <returns>The canonical JSON.</returns>
    public static string Serialize(ScheduledTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        ScheduledTaskValidator.Validate(task);
        var normalizedTask = task with
        {
            FileSystemAccess = task.FileSystemAccess
                .Select(
                    static rule => rule with
                    {
                        Path = ScheduledTaskValidator.NormalizeAccessPath(rule.Path),
                    })
                .ToArray(),
        };
        return JsonSerializer.Serialize(normalizedTask, Options);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
