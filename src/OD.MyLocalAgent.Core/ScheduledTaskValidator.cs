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

        if (task.CreatedUtc is not null &&
            task.UpdatedUtc is not null &&
            task.CreatedUtc > task.UpdatedUtc)
        {
            throw new TaskValidationException(
                "Task 'createdUtc' must not be later than 'updatedUtc'.");
        }

        if (task.GenerationOptions is not null &&
            GenerationPolicyValidation.ValidateOverrides(task.GenerationOptions)
                is { } generationError)
        {
            throw new TaskValidationException(
                $"Task 'generationOptions' is invalid. {generationError}");
        }

        ValidateModelName(task.Model, "Task 'model'");

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
                    $"Task tool group '{group}' is unknown. Expected one of: {string.Join(", ", ToolGroups.TaskWhitelist.Select(static definition => definition.Name))}.");
            }

            if (!toolGroups.Add(group))
            {
                throw new TaskValidationException($"Task tool group '{group}' is duplicated.");
            }
        }

        if (task.Capabilities is null)
        {
            throw new TaskValidationException("Task 'capabilities' must be an array.");
        }

        var capabilities = new HashSet<TaskCapability>();
        foreach (var capability in task.Capabilities)
        {
            if (capability == TaskCapability.None || !Enum.IsDefined(capability))
            {
                throw new TaskValidationException($"Task capability '{capability}' is unknown.");
            }

            if (!capabilities.Add(capability))
            {
                throw new TaskValidationException($"Task capability '{capability}' is duplicated.");
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

        IReadOnlyList<string> knowledgeTags;
        try
        {
            knowledgeTags = KnowledgeTagPolicy.NormalizeScope(task.KnowledgeAccess?.AllowedTags);
        }
        catch (ArgumentException exception)
        {
            throw new TaskValidationException($"Task 'knowledgeAccess' is invalid. {exception.Message}", exception);
        }

        if (knowledgeTags.Count > 0 && !toolGroups.Contains(ToolGroups.Knowledge))
        {
            throw new TaskValidationException("Task 'knowledgeAccess' requires the 'knowledge' tool group.");
        }

        ValidateWorkflow(task.Workflow, toolGroups, knowledgeTags);
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
            new HashSet<string>(toolGroups, StringComparer.OrdinalIgnoreCase),
            []);
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
        IReadOnlySet<string> toolGroups,
        IReadOnlyList<string> taskKnowledgeTags)
    {
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < workflow.Count; index++)
        {
            var step = workflow[index]
                ?? throw new WorkflowValidationException($"Workflow step {index + 1} cannot be null.");
            var definition = ToolMap.Find(step.Step)
                ?? throw new WorkflowValidationException(
                    $"Workflow step {index + 1} references unknown alias '{step.Step}'.");

            if (!definition.IsAiStep && !definition.IsControlFlowStep && !toolGroups.Contains(definition.ToolGroup))
            {
                throw new WorkflowValidationException(
                    $"Workflow alias '{step.Step}' requires whitelisted tool group '{definition.ToolGroup}'.");
            }

            ValidatePromptTools(step, toolGroups, taskKnowledgeTags);

            if (!definition.IsAiStep && step.GenerationOptions is not null)
            {
                throw new WorkflowValidationException(
                    $"Workflow step '{step.EffectiveId}' cannot define 'generationOptions' because alias '{step.Step}' is not an AI step.");
            }

            if (step.Model is not null &&
                !string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase))
            {
                throw new WorkflowValidationException(
                    $"Workflow step '{step.EffectiveId}' can define 'model' only for alias '{ToolMap.PromptAlias}'.");
            }

            ValidateModelName(step.Model, $"Workflow step '{step.EffectiveId}' model");

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

            if (step.FallbackOutput is not null &&
                !string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase))
            {
                throw new WorkflowValidationException(
                    $"Workflow step '{step.EffectiveId}' can define 'fallbackOutput' only for alias '{ToolMap.PromptAlias}'.");
            }

            if (step.FallbackOutput is not null && step.ResponseSchema is null)
            {
                throw new WorkflowValidationException(
                    $"Workflow step '{step.EffectiveId}' requires 'responseSchema' when defining 'fallbackOutput'.");
            }

            if (step.FallbackOutput is { } fallbackOutput && step.ResponseSchema is { } fallbackSchema)
            {
                try
                {
                    WorkflowResponseSchemaValidator.ValidateResponse(fallbackOutput.GetRawText(), fallbackSchema);
                }
                catch (WorkflowStructuredResponseException exception)
                {
                    throw new WorkflowValidationException(
                        $"Workflow step '{step.EffectiveId}' has an invalid 'fallbackOutput'. {exception.Message}",
                        exception);
                }
            }

            ValidateForEach(step, definition, identifiers);

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

            if (string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase) &&
                step.Args.TryGetProperty("timeout", out var timeout) &&
                (timeout.ValueKind != JsonValueKind.Number ||
                 !timeout.TryGetInt32(out var timeoutMinutes) ||
                 timeoutMinutes is < 1 or > 999))
            {
                throw new WorkflowValidationException(
                    "Workflow argument 'timeout' must be an integer from 1 to 999 minutes.");
            }

            if (string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase) &&
                step.Args.TryGetProperty("thinking", out var thinking) &&
                thinking.ValueKind is not JsonValueKind.True and not JsonValueKind.False and not JsonValueKind.String)
            {
                throw new WorkflowValidationException(
                    "Workflow argument 'thinking' must be a boolean or a non-empty provider-specific effort string.");
            }

            if (string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase) &&
                step.Args.TryGetProperty("thinking", out thinking) &&
                thinking.ValueKind == JsonValueKind.String &&
                GenerationPolicyValidation.ValidateReasoningEffort(thinking.GetString()) is { } thinkingError)
            {
                throw new WorkflowValidationException(
                    $"Workflow argument 'thinking' is invalid. {thinkingError}");
            }

            ValidateArguments(step, definition);
            if (step.Step == "gmailDecision.readClassify") GmailDecisionContractValidator.Validate(step.Args);
            if (definition.IsControlFlowStep)
            {
                _ = WorkflowDecisionDefinition.Parse(step.Args);
            }

            if (step.Next is not null && string.IsNullOrWhiteSpace(step.Next))
            {
                throw new WorkflowValidationException($"Workflow step '{step.EffectiveId}' next target cannot be empty.");
            }

            if (step.ResponseSchema is { } responseSchema)
            {
                if (!string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase))
                {
                    throw new WorkflowValidationException("Workflow property 'responseSchema' is valid only for 'ai.prompt'.");
                }

                if (step.ToolAliases.Count > 0)
                {
                    throw new WorkflowValidationException($"Workflow step '{step.EffectiveId}' cannot combine 'responseSchema' with 'toolAliases' in Stage 29.");
                }

                WorkflowResponseSchemaValidator.ValidateSchema(responseSchema);
            }

            ValidatePlaceholders(step, identifiers);
            identifiers.Add(step.EffectiveId);
        }

        ValidateRouting(workflow);
    }

    private static void ValidateRouting(IReadOnlyList<WorkflowStepDefinition> workflow)
    {
        if (workflow.Count == 0)
        {
            return;
        }

        const string end = "@end";
        var indexes = workflow
            .Select((step, index) => (step.EffectiveId, index))
            .ToDictionary(static item => item.EffectiveId, static item => item.index, StringComparer.OrdinalIgnoreCase);
        var edges = new List<int>[workflow.Count];
        for (var index = 0; index < workflow.Count; index++)
        {
            var step = workflow[index];
            var targets = new List<string>();
            if (string.Equals(step.Step, "flow.decision", StringComparison.OrdinalIgnoreCase))
            {
                var decision = WorkflowDecisionDefinition.Parse(step.Args);
                targets.Add(decision.Target);
                if (decision.Fallback is not null)
                {
                    targets.Add(decision.Fallback);
                }
            }

            targets.Add(step.Next ?? (index + 1 < workflow.Count ? workflow[index + 1].EffectiveId : end));
            edges[index] = [];
            foreach (var target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(target, end, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!indexes.TryGetValue(target, out var targetIndex))
                {
                    throw new WorkflowValidationException($"Workflow step '{step.EffectiveId}' targets unknown step id '{target}'.");
                }

                edges[index].Add(targetIndex);
            }
        }

        var reachable = new bool[workflow.Count];
        MarkReachable(0, edges, reachable);
        var unreachable = workflow.Where((_, index) => !reachable[index]).Select(static step => step.EffectiveId).ToArray();
        if (unreachable.Length > 0)
        {
            throw new WorkflowValidationException($"Workflow contains unreachable step(s): {string.Join(", ", unreachable)}.");
        }

        var state = new byte[workflow.Count];
        var stack = new List<int>();
        for (var index = 0; index < workflow.Count; index++)
        {
            if (state[index] == 0 && TryFindCycle(index, edges, state, stack, out var cycle))
            {
                var names = cycle.Select(item => workflow[item].EffectiveId);
                throw new WorkflowValidationException($"Workflow contains a cycle: {string.Join(" → ", names)}.");
            }
        }

        ValidateGuaranteedOutputs(workflow, indexes, edges);
    }

    private static void ValidateGuaranteedOutputs(
        IReadOnlyList<WorkflowStepDefinition> workflow,
        IReadOnlyDictionary<string, int> indexes,
        IReadOnlyList<List<int>> edges)
    {
        var predecessors = Enumerable.Range(0, workflow.Count).Select(static _ => new List<int>()).ToArray();
        for (var source = 0; source < edges.Count; source++)
        {
            foreach (var target in edges[source])
            {
                predecessors[target].Add(source);
            }
        }

        var all = Enumerable.Range(0, workflow.Count).ToHashSet();
        var dominators = new HashSet<int>[workflow.Count];
        dominators[0] = [0];
        for (var index = 1; index < workflow.Count; index++)
        {
            dominators[index] = [.. all];
        }

        bool changed;
        do
        {
            changed = false;
            for (var index = 1; index < workflow.Count; index++)
            {
                var intersection = predecessors[index].Count == 0
                    ? new HashSet<int>()
                    : new HashSet<int>(dominators[predecessors[index][0]]);
                foreach (var predecessor in predecessors[index].Skip(1))
                {
                    intersection.IntersectWith(dominators[predecessor]);
                }

                intersection.Add(index);
                if (!intersection.SetEquals(dominators[index]))
                {
                    dominators[index] = intersection;
                    changed = true;
                }
            }
        }
        while (changed);

        for (var consumer = 0; consumer < workflow.Count; consumer++)
        {
            var texts = workflow[consumer].Args.ValueKind == JsonValueKind.Object
                ? EnumerateStrings(workflow[consumer].Args)
                : [];
            if (workflow[consumer].ForEach is { } forEach)
            {
                texts = texts.Append(forEach);
            }

            foreach (var placeholder in texts.SelectMany(WorkflowPlaceholderCatalog.Extract))
            {
                var marker = placeholder.IndexOf(".output", StringComparison.OrdinalIgnoreCase);
                if (marker <= 0)
                {
                    continue;
                }

                var producerId = placeholder[..marker];
                if (string.Equals(producerId, "previous", StringComparison.OrdinalIgnoreCase) ||
                    !indexes.TryGetValue(producerId, out var producer))
                {
                    continue;
                }

                if (ToolMap.Find(workflow[producer].Step)?.IsControlFlowStep == true)
                {
                    throw new WorkflowValidationException(
                        $"Placeholder '{{{{{placeholder}}}}}' references control-flow step '{producerId}', which does not publish workflow output.");
                }

                if (!dominators[consumer].Contains(producer))
                {
                    throw new WorkflowValidationException(
                        $"Placeholder '{{{{{placeholder}}}}}' is not guaranteed because step '{producerId}' does not run on every route to '{workflow[consumer].EffectiveId}'.");
                }
            }
        }
    }

    private static void MarkReachable(int index, IReadOnlyList<List<int>> edges, bool[] reachable)
    {
        if (reachable[index])
        {
            return;
        }

        reachable[index] = true;
        foreach (var target in edges[index])
        {
            MarkReachable(target, edges, reachable);
        }
    }

    private static bool TryFindCycle(
        int index,
        IReadOnlyList<List<int>> edges,
        byte[] state,
        List<int> stack,
        out IReadOnlyList<int> cycle)
    {
        state[index] = 1;
        stack.Add(index);
        foreach (var target in edges[index])
        {
            if (state[target] == 0 && TryFindCycle(target, edges, state, stack, out cycle))
            {
                return true;
            }

            if (state[target] == 1)
            {
                var start = stack.IndexOf(target);
                cycle = [.. stack.Skip(start), target];
                return true;
            }
        }

        stack.RemoveAt(stack.Count - 1);
        state[index] = 2;
        cycle = [];
        return false;
    }

    private static void ValidatePromptTools(
        WorkflowStepDefinition step,
        IReadOnlySet<string> toolGroups,
        IReadOnlyList<string> taskKnowledgeTags)
    {
        var isPrompt = string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase);
        if (!isPrompt && (step.ToolAliases.Count > 0 || step.KnowledgeAccess is not null))
        {
            throw new WorkflowValidationException("Properties 'toolAliases' and 'knowledgeAccess' are valid only for 'ai.prompt'.");
        }

        if (!isPrompt)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in step.ToolAliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || !seen.Add(alias))
            {
                throw new WorkflowValidationException("Prompt-step tool aliases must be non-empty and unique.");
            }

            var selected = ToolCatalog.Default.Find(alias)
                ?? throw new WorkflowValidationException($"Prompt step references unknown tool alias '{alias}'.");
            if (!selected.IsAgentTool)
            {
                throw new WorkflowValidationException($"Prompt-step alias '{alias}' is not an agent-callable tool.");
            }

            if (!toolGroups.Contains(selected.ToolGroup))
            {
                throw new WorkflowValidationException($"Prompt-step alias '{alias}' requires whitelisted tool group '{selected.ToolGroup}'.");
            }
        }

        var selectsKnowledge = step.ToolAliases.Any(alias =>
            string.Equals(alias, "knowledge.search", StringComparison.OrdinalIgnoreCase));
        if (step.KnowledgeAccess is not null && !selectsKnowledge)
        {
            throw new WorkflowValidationException("Prompt-step 'knowledgeAccess' requires alias 'knowledge.search'.");
        }

        if (step.KnowledgeAccess is not null)
        {
            try
            {
                _ = KnowledgeTagPolicy.Narrow(taskKnowledgeTags, step.KnowledgeAccess.AllowedTags);
            }
            catch (Exception exception) when (exception is ArgumentException or KnowledgeAuthorizationException)
            {
                throw new WorkflowValidationException($"Prompt-step 'knowledgeAccess' is invalid: {exception.Message}");
            }
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
            var argument = definition.ArgumentDefinitions.Single(item => string.Equals(item.Name, property.Name, StringComparison.OrdinalIgnoreCase));
            if (property.Value.ValueKind == JsonValueKind.Null ? !argument.AllowsNull : !IsCompatible(property.Value, argument.ValueType))
            {
                throw new WorkflowValidationException(
                    $"Workflow argument '{property.Name}' for '{step.Step}' must be {DescribeType(argument.ValueType)}.");
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
                        out var error,
                        allowForEach: step.ForEach is not null))
                {
                    throw new WorkflowValidationException(error!);
                }
            }
        }
    }

    private static void ValidateForEach(
        WorkflowStepDefinition step,
        ToolDefinition definition,
        IReadOnlySet<string> availableStepIds)
    {
        if (step.ForEach is null)
        {
            return;
        }

        if (!definition.SupportsForEach)
        {
            throw new WorkflowValidationException(
                $"Workflow alias '{step.Step}' does not support 'forEach'.");
        }

        var source = step.ForEach.Trim();
        var placeholders = WorkflowPlaceholderCatalog.Extract(source);
        if (placeholders.Count != 1 ||
            source.Length < 4 ||
            !source.StartsWith("{{", StringComparison.Ordinal) ||
            !source.EndsWith("}}", StringComparison.Ordinal) ||
            !string.Equals(source[2..^2].Trim(), placeholders[0], StringComparison.Ordinal))
        {
            throw new WorkflowValidationException(
                $"Workflow step '{step.EffectiveId}' property 'forEach' must contain exactly one output placeholder.");
        }

        var placeholder = placeholders[0];
        var outputMarker = placeholder.IndexOf(".output", StringComparison.OrdinalIgnoreCase);
        string? error = null;
        if (placeholder.StartsWith("previous.output", StringComparison.OrdinalIgnoreCase) &&
            availableStepIds.Count == 0)
        {
            error = $"Workflow step '{step.EffectiveId}' property 'forEach' cannot select previous.output because no prior step is available.";
        }

        if (error is not null ||
            outputMarker <= 0 ||
            !WorkflowPlaceholderCatalog.IsValid(
                placeholder,
                availableStepIds,
                out error))
        {
            throw new WorkflowValidationException(
                error ?? $"Workflow step '{step.EffectiveId}' property 'forEach' must select a prior step output.");
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

    private static void ValidateModelName(string? modelName, string property)
    {
        if (modelName is not null && string.IsNullOrWhiteSpace(modelName))
        {
            throw new TaskValidationException($"{property} cannot be empty or whitespace.");
        }
    }

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TaskIdPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkflowIdPattern();
}
