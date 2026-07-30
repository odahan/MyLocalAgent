using System.Text.Json;
using System.Text.RegularExpressions;
using Cronos;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Validates persisted scheduled-task and workflow contracts.
/// </summary>
public static partial class ScheduledTaskValidator
{
    /// <summary>
    /// Validates a scheduled task and throws a typed exception when it is invalid.
    /// </summary>
    /// <param name="task">The task to validate.</param>
    public static void Validate(ScheduledTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (string.IsNullOrWhiteSpace(task.Id) || !TaskIdPattern().IsMatch(task.Id))
        {
            throw new TaskValidationException(
                "Task 'id' is required and must match [a-z0-9-]+.");
        }

        if (string.IsNullOrWhiteSpace(task.Name))
        {
            throw new TaskValidationException("Task 'name' is required.");
        }

        if (string.IsNullOrWhiteSpace(task.Schedule))
        {
            throw new TaskValidationException("Task 'schedule' is required.");
        }

        try
        {
            _ = CronExpression.Parse(task.Schedule);
        }
        catch (CronFormatException exception)
        {
            throw new TaskValidationException(
                $"Task schedule '{task.Schedule}' is not a valid five-field cron expression.",
                exception);
        }

        if (task.Tools is null)
        {
            throw new TaskValidationException("Task 'tools' must be an array.");
        }

        var toolGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in task.Tools)
        {
            if (string.IsNullOrWhiteSpace(group) || !ToolGroups.All.Contains(group))
            {
                throw new TaskValidationException(
                    $"Task tool group '{group}' is unknown. Expected one of: {string.Join(", ", ToolGroups.All)}.");
            }

            if (!toolGroups.Add(group))
            {
                throw new TaskValidationException($"Task tool group '{group}' is duplicated.");
            }
        }

        ValidateFileSystemAccess(task.FileSystemAccess);

        if (task.Workflow is null)
        {
            throw new TaskValidationException("Task 'workflow' must be an array.");
        }

        if (string.IsNullOrWhiteSpace(task.Prompt) && task.Workflow.Count == 0)
        {
            throw new TaskValidationException(
                "Task 'prompt' may be empty only when the workflow contains at least one step.");
        }

        ValidateWorkflow(task.Workflow, toolGroups);
    }

    private static void ValidateFileSystemAccess(
        IReadOnlyList<FileSystemAccessRule>? rules)
    {
        if (rules is null)
        {
            throw new TaskValidationException(
                "Task 'fileSystemAccess' must be an array.");
        }

        var normalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index]
                ?? throw new TaskValidationException(
                    $"File-system access rule {index + 1} cannot be null.");
            if (string.IsNullOrWhiteSpace(rule.Path))
            {
                throw new TaskValidationException(
                    $"File-system access rule {index + 1} requires a path.");
            }

            if (!Enum.IsDefined(rule.Access))
            {
                throw new TaskValidationException(
                    $"File-system access rule {index + 1} has an unsupported access mode.");
            }

            var expandedPath = Environment.ExpandEnvironmentVariables(rule.Path.Trim());
            if (!Path.IsPathRooted(expandedPath))
            {
                throw new TaskValidationException(
                    $"File-system access path '{rule.Path}' must be absolute.");
            }

            if (expandedPath.IndexOfAny(['*', '?']) >= 0)
            {
                throw new TaskValidationException(
                    $"File-system access path '{rule.Path}' cannot contain wildcards.");
            }

            string normalizedPath;
            try
            {
                normalizedPath = NormalizeAccessPath(expandedPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                    NotSupportedException or
                    PathTooLongException)
            {
                throw new TaskValidationException(
                    $"File-system access path '{rule.Path}' is invalid.",
                    exception);
            }

            if (!normalizedPaths.Add(normalizedPath))
            {
                throw new TaskValidationException(
                    $"File-system access path '{rule.Path}' is duplicated.");
            }

            if (File.Exists(normalizedPath))
            {
                throw new TaskValidationException(
                    $"File-system access path '{rule.Path}' must identify a directory.");
            }
        }
    }

    internal static string NormalizeAccessPath(string path)
    {
        var fullPath = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(path.Trim()));
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static void ValidateWorkflow(
        IReadOnlyList<WorkflowStepDefinition> workflow,
        IReadOnlySet<string> toolGroups)
    {
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < workflow.Count; index++)
        {
            var step = workflow[index]
                ?? throw new WorkflowValidationException($"Workflow step {index + 1} cannot be null.");
            var definition = ToolMap.Find(step.Step)
                ?? throw new WorkflowValidationException(
                    $"Workflow step {index + 1} references unknown alias '{step.Step}'.");

            if (!definition.IsAiStep && !toolGroups.Contains(definition.ToolGroup))
            {
                throw new WorkflowValidationException(
                    $"Workflow alias '{step.Step}' requires whitelisted tool group '{definition.ToolGroup}'.");
            }

            if (step.Args.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Object)
            {
                throw new WorkflowValidationException(
                    $"Workflow arguments for '{step.Step}' must be a JSON object.");
            }

            if (string.IsNullOrWhiteSpace(step.EffectiveId) ||
                !WorkflowIdPattern().IsMatch(step.EffectiveId))
            {
                throw new WorkflowValidationException(
                    $"Workflow step id '{step.EffectiveId}' contains unsupported characters.");
            }

            if (!identifiers.Add(step.EffectiveId))
            {
                throw new WorkflowValidationException(
                    $"Workflow step id '{step.EffectiveId}' is duplicated.");
            }

            if (step.RetryCount is < 0)
            {
                throw new WorkflowValidationException(
                    $"Workflow step '{step.Step}' has a negative retry count.");
            }

            if (step.RetryBackoffSeconds is < 0)
            {
                throw new WorkflowValidationException(
                    $"Workflow step '{step.Step}' has a negative retry backoff.");
            }

            if (string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase) &&
                (step.Args.ValueKind != JsonValueKind.Object ||
                 !step.Args.TryGetProperty("prompt", out var prompt) ||
                 prompt.ValueKind != JsonValueKind.String))
            {
                throw new WorkflowValidationException(
                    "Workflow alias 'ai.prompt' requires string argument 'prompt'.");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TaskIdPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkflowIdPattern();
}
