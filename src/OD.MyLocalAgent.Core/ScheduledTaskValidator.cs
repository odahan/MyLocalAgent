using System.Text.Json;
using System.Text.RegularExpressions;
using Cronos;
using OD.MyLocalAgent.Ollama;

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

        if (task.GenerationOptions is not null &&
            GenerationPolicyValidation.ValidateOverrides(task.GenerationOptions)
                is { } generationError)
        {
            throw new TaskValidationException(
                $"Task 'generationOptions' is invalid. {generationError}");
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

        ValidateWorkflowDefinitions(task.Workflow, toolGroups);
    }

    /// <summary>
    /// Validates workflow definitions against Tool Map metadata and a tool whitelist.
    /// </summary>
    /// <param name="workflow">The ordered workflow definitions.</param>
    /// <param name="toolGroups">The available task tool groups.</param>
    public static void ValidateWorkflowDefinitions(
        IReadOnlyList<WorkflowStepDefinition> workflow,
        IEnumerable<string> toolGroups)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(toolGroups);
        ValidateWorkflow(
            workflow,
            new HashSet<string>(toolGroups, StringComparer.OrdinalIgnoreCase));
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

    /// <summary>
    /// Expands and normalizes an absolute task authorization path.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized path, preserving a volume root separator.</returns>
    public static string NormalizeAccessPath(string path)
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

            if (!definition.IsAiStep && step.GenerationOptions is not null)
            {
                throw new WorkflowValidationException(
                    $"Workflow step '{step.EffectiveId}' cannot define 'generationOptions' because alias '{step.Step}' is not an AI step.");
            }

            if (step.GenerationOptions is not null &&
                GenerationPolicyValidation.ValidateOverrides(step.GenerationOptions)
                    is { } generationError)
            {
                throw new WorkflowValidationException(
                    $"Workflow step '{step.EffectiveId}' has invalid 'generationOptions'. {generationError}");
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

            if (identifiers.Contains(step.EffectiveId))
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

            if (string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase))
            {
                if (step.Args.ValueKind != JsonValueKind.Object ||
                    !step.Args.TryGetProperty("prompt", out var prompt) ||
                    prompt.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(prompt.GetString()))
                {
                    throw new WorkflowValidationException(
                        "Workflow alias 'ai.prompt' requires a non-empty string argument 'prompt'.");
                }

                if (prompt.GetString()!.Contains('\r', StringComparison.Ordinal))
                {
                    throw new WorkflowValidationException(
                        "Workflow argument 'prompt' must use LF line endings without carriage returns.");
                }
            }

            if (string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase) &&
                step.Args.TryGetProperty("includeContext", out var includeContext) &&
                includeContext.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                throw new WorkflowValidationException(
                    "Workflow argument 'includeContext' must be a boolean.");
            }

            ValidateArguments(step, definition);
            ValidatePlaceholders(step, identifiers);
            identifiers.Add(step.EffectiveId);
        }
    }

    private static void ValidateArguments(
        WorkflowStepDefinition step,
        ToolDefinition definition)
    {
        var supplied = step.Args.ValueKind == JsonValueKind.Object
            ? step.Args.EnumerateObject().ToArray()
            : [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in supplied)
        {
            if (!seen.Add(property.Name))
            {
                throw new WorkflowValidationException(
                    $"Workflow alias '{step.Step}' declares argument '{property.Name}' more than once.");
            }

            if (!definition.Arguments.Contains(
                    property.Name,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new WorkflowValidationException(
                    $"Workflow alias '{step.Step}' does not define argument '{property.Name}'.");
            }
        }

        foreach (var argument in definition.Arguments)
        {
            var optional = definition.OptionalArguments.Contains(
                argument,
                StringComparer.OrdinalIgnoreCase);
            if (!optional &&
                !supplied.Any(
                    property => string.Equals(
                        property.Name,
                        argument,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new WorkflowValidationException(
                    $"Workflow alias '{step.Step}' requires argument '{argument}'.");
            }
        }

        foreach (var property in supplied)
        {
            var expectedType = GetArgumentType(definition, property.Name);
            if (!IsCompatible(property.Value, expectedType))
            {
                throw new WorkflowValidationException(
                    $"Workflow argument '{property.Name}' for '{step.Step}' must be {DescribeType(expectedType)}.");
            }
        }
    }

    private static void ValidatePlaceholders(
        WorkflowStepDefinition step,
        IReadOnlySet<string> availableStepIds)
    {
        if (step.Args.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var text in EnumerateStrings(step.Args))
        {
            var textWithoutValidPlaceholders = WorkflowPlaceholderCatalog.Replace(
                text,
                static _ => string.Empty);
            if (textWithoutValidPlaceholders.Contains("{{", StringComparison.Ordinal) ||
                textWithoutValidPlaceholders.Contains("}}", StringComparison.Ordinal))
            {
                throw new WorkflowValidationException(
                    $"Workflow alias '{step.Step}' contains malformed placeholder braces.");
            }

            foreach (var placeholder in WorkflowPlaceholderCatalog.Extract(text))
            {
                if (!WorkflowPlaceholderCatalog.IsValid(
                        placeholder,
                        availableStepIds,
                        out var error))
                {
                    throw new WorkflowValidationException(error!);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var value in EnumerateStrings(property.Value))
                    {
                        yield return value;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var value in EnumerateStrings(item))
                    {
                        yield return value;
                    }
                }

                break;
            case JsonValueKind.String:
                yield return element.GetString() ?? string.Empty;
                break;
        }
    }

    private static Type GetArgumentType(
        ToolDefinition definition,
        string argumentName)
    {
        if (definition.IsAiStep)
        {
            return string.Equals(
                argumentName,
                "includeContext",
                StringComparison.OrdinalIgnoreCase)
                ? typeof(bool)
                : typeof(string);
        }

        var method = definition.DeclaringType.GetMethod(definition.MethodName!)
            ?? throw new InvalidOperationException(
                $"Tool Map method '{definition.MethodName}' is unavailable.");
        return method.GetParameters()
            .Single(
                parameter => string.Equals(
                    parameter.Name,
                    argumentName,
                    StringComparison.OrdinalIgnoreCase))
            .ParameterType;
    }

    private static bool IsCompatible(JsonElement value, Type expectedType)
    {
        var underlying = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
        if (Nullable.GetUnderlyingType(expectedType) is not null &&
            value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (underlying == typeof(string))
        {
            return value.ValueKind == JsonValueKind.String;
        }

        if (underlying == typeof(bool))
        {
            return value.ValueKind is JsonValueKind.True or JsonValueKind.False;
        }

        if (underlying == typeof(int))
        {
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _);
        }

        return true;
    }

    private static string DescribeType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(string))
        {
            return "a string";
        }

        if (underlying == typeof(bool))
        {
            return "a boolean";
        }

        if (underlying == typeof(int))
        {
            return "an integer";
        }

        return "a compatible JSON value";
    }

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TaskIdPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkflowIdPattern();
}
