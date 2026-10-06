using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Creates agent functions and direct workflow invocations from the canonical tool catalog.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly IToolCatalog _catalog;
    private readonly IToolTargetActivator _targetActivator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolRegistry"/> class.
    /// </summary>
    /// <param name="catalog">The canonical tool catalog.</param>
    /// <param name="targetActivator">The generic target activator.</param>
    public ToolRegistry(IToolCatalog catalog, IToolTargetActivator targetActivator)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(targetActivator);
        _catalog = catalog;
        _targetActivator = targetActivator;
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolDefinition> Definitions => _catalog.Definitions;

    /// <inheritdoc />
    public AITool[] GetAgentTools(IEnumerable<string> enabledToolGroups)
        => GetAgentTools(enabledToolGroups, ToolExecutionContext.Taskless);

    /// <inheritdoc />
    public AITool[] GetAgentTools(
        IEnumerable<string> enabledToolGroups,
        ToolExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        var whitelist = NormalizeWhitelist(enabledToolGroups);
        var functions = CreateFunctions(whitelist, executionContext);
        return _catalog.Definitions
            .Where(definition =>
                definition.IsAgentTool &&
                whitelist.Contains(definition.ToolGroup) &&
                IsAuthorized(definition, executionContext))
            .Select(definition => (AITool)functions[definition.Alias])
            .ToArray();
    }

    /// <inheritdoc />
    public AITool[] GetAgentToolsByAliases(
        IEnumerable<string> aliases,
        IEnumerable<string> enabledToolGroups,
        ToolExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(executionContext);
        var whitelist = NormalizeWhitelist(enabledToolGroups);
        var selected = new List<ToolDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || !seen.Add(alias))
            {
                throw new WorkflowValidationException("Prompt-step tool aliases must be non-empty and unique.");
            }

            var definition = _catalog.Find(alias)
                ?? throw new WorkflowValidationException($"Unknown Tool Map alias '{alias}'.");
            if (!definition.IsAgentTool)
            {
                throw new WorkflowValidationException($"Alias '{alias}' is not an agent-callable tool.");
            }

            if (!whitelist.Contains(definition.ToolGroup))
            {
                throw new WorkflowValidationException($"Alias '{alias}' requires whitelisted tool group '{definition.ToolGroup}'.");
            }

            EnsureAuthorized(definition, executionContext);
            selected.Add(definition);
        }

        var functions = CreateFunctions(selected, executionContext);
        return selected.Select(definition => (AITool)functions[definition.Alias]).ToArray();
    }

    /// <inheritdoc />
    public bool TryGetDefinition(string alias, out ToolDefinition? definition)
    {
        definition = _catalog.Find(alias);
        return definition is not null;
    }

    /// <inheritdoc />
    public async Task<string> InvokeAsync(
        string alias,
        JsonElement arguments,
        IEnumerable<string> enabledToolGroups,
        CancellationToken cancellationToken) =>
        await InvokeAsync(
            alias,
            arguments,
            enabledToolGroups,
            ToolExecutionContext.Taskless,
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<string> InvokeAsync(
        string alias,
        JsonElement arguments,
        IEnumerable<string> enabledToolGroups,
        ToolExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(executionContext);
        var definition = _catalog.Find(alias)
            ?? throw new WorkflowValidationException($"Unknown Tool Map alias '{alias}'.");
        if (definition.Method is null)
        {
            throw new WorkflowValidationException(
                $"Alias '{alias}' is an AI workflow step, not an invocable tool.");
        }

        var whitelist = NormalizeWhitelist(enabledToolGroups);
        if (!whitelist.Contains(definition.ToolGroup))
        {
            throw new WorkflowValidationException(
                $"Alias '{alias}' requires whitelisted tool group '{definition.ToolGroup}'.");
        }

        EnsureAuthorized(definition, executionContext);

        if (arguments.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Tool arguments must be a JSON object.",
                nameof(arguments));
        }

        var suppliedArguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in arguments.EnumerateObject())
            {
                if (!definition.Arguments.Contains(
                        property.Name,
                        StringComparer.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Tool '{alias}' does not accept argument '{property.Name}'. Expected: {FormatExpected(definition)}.",
                        nameof(arguments));
                }

                if (!suppliedArguments.TryAdd(property.Name, property.Value.Clone()))
                    throw new ArgumentException("Duplicate tool argument names are not allowed.", nameof(arguments));
            }
        }

        var missing = definition.Arguments
            .Where(argument =>
                !definition.OptionalArguments.Contains(
                    argument,
                    StringComparer.OrdinalIgnoreCase) &&
                !suppliedArguments.ContainsKey(argument))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"Tool '{alias}' is missing required arguments: {string.Join(", ", missing)}. Expected: {FormatExpected(definition)}.",
                nameof(arguments));
        }

        var functions = CreateFunctions([definition], executionContext);
        var result = await functions[definition.Alias]
            .InvokeAsync(new AIFunctionArguments(suppliedArguments), cancellationToken)
            .ConfigureAwait(false);
        return result switch
        {
            null => "null",
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element =>
                element.GetString() ?? string.Empty,
            JsonElement element => element.GetRawText(),
            _ => JsonSerializer.Serialize(result, ToolJson.Options),
        };
    }

    private Dictionary<string, AIFunction> CreateFunctions(
        HashSet<string> whitelist,
        ToolExecutionContext executionContext)
    {
        var targets = new Dictionary<Type, object>();
        var functions = new Dictionary<string, AIFunction>(StringComparer.OrdinalIgnoreCase);

        var enabledDefinitions = _catalog.Definitions
            .Where(def =>
                def.IsAgentTool &&
                whitelist.Contains(def.ToolGroup) &&
                IsAuthorized(def, executionContext));

        foreach (var definition in enabledDefinitions)
        {
            if (!targets.TryGetValue(definition.DeclaringType, out var target))
            {
                target = _targetActivator.CreateTarget(definition.DeclaringType, executionContext);
                targets[definition.DeclaringType] = target;
            }

            var method = definition.Method
                ?? definition.DeclaringType.GetMethod(
                    definition.MethodName!,
                    BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException(
                    $"Tool method '{definition.DeclaringType.Name}.{definition.MethodName}' does not exist.");

            functions.Add(
                definition.Alias,
                AIFunctionFactory.Create(
                    method,
                    target,
                    definition.AgentFunctionName!,
                    definition.Description,
                    ToolJson.Options));
        }

        return functions;
    }

    private Dictionary<string, AIFunction> CreateFunctions(
        IEnumerable<ToolDefinition> definitions,
        ToolExecutionContext executionContext)
    {
        var targets = new Dictionary<Type, object>();
        var functions = new Dictionary<string, AIFunction>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (!targets.TryGetValue(definition.DeclaringType, out var target))
            {
                target = _targetActivator.CreateTarget(definition.DeclaringType, executionContext);
                targets.Add(definition.DeclaringType, target);
            }

            var method = definition.Method
                ?? throw new InvalidOperationException($"Tool method for '{definition.Alias}' is unavailable.");
            functions.Add(
                definition.Alias,
                AIFunctionFactory.Create(method, target, definition.AgentFunctionName ?? ToolCatalog.DeriveAgentFunctionName(definition.Alias), definition.Description, ToolJson.Options));
        }

        return functions;
    }

    private HashSet<string> NormalizeWhitelist(
        IEnumerable<string> enabledToolGroups)
    {
        ArgumentNullException.ThrowIfNull(enabledToolGroups);
        var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in enabledToolGroups)
        {
            if (string.IsNullOrWhiteSpace(group) || !_catalog.TaskWhitelistNames.Contains(group))
            {
                throw new ArgumentException(
                    $"Unknown tool group '{group}'.",
                    nameof(enabledToolGroups));
            }

            whitelist.Add(group);
        }

        return whitelist;
    }

    private static string FormatExpected(ToolDefinition definition) =>
        definition.Arguments.Count == 0
            ? "(none)"
            : string.Join(
                ", ",
                definition.Arguments.Select(
                    argument => definition.OptionalArguments.Contains(
                        argument,
                        StringComparer.OrdinalIgnoreCase)
                        ? $"{argument} (optional)"
                        : argument));

    private static bool IsAuthorized(
        ToolDefinition definition,
        ToolExecutionContext context) =>
        definition.RequiredCapability is not { } capability ||
        context.Capabilities.Contains(capability);

    private static void EnsureAuthorized(
        ToolDefinition definition,
        ToolExecutionContext context)
    {
        if (definition.WorkflowOnly &&
            (string.IsNullOrWhiteSpace(context.TaskId) ||
             context.Surface is not (ToolInvocationSurface.Workflow or ToolInvocationSurface.WorkflowTest)))
        {
            throw new WorkflowValidationException($"Alias '{definition.Alias}' requires an authorized workflow context.");
        }

        if (definition.RequiredCapability is { } capability &&
            !context.Capabilities.Contains(capability))
        {
            throw new TaskAuthorizationException(context.TaskId, capability);
        }
    }
}
