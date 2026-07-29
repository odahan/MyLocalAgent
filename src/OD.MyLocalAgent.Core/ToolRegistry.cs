using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Creates agent functions and direct workflow invocations from the static Tool Map.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyDictionary<string, AIFunction> _functionsByAlias;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolRegistry"/> class.
    /// </summary>
    public ToolRegistry(
        GmailTool gmailTool,
        CalendarTool calendarTool,
        DropboxTool dropboxTool,
        FileSystemTool fileSystemTool,
        HttpTool httpTool,
        WebSearchTool webSearchTool)
    {
        ArgumentNullException.ThrowIfNull(gmailTool);
        ArgumentNullException.ThrowIfNull(calendarTool);
        ArgumentNullException.ThrowIfNull(dropboxTool);
        ArgumentNullException.ThrowIfNull(fileSystemTool);
        ArgumentNullException.ThrowIfNull(httpTool);
        ArgumentNullException.ThrowIfNull(webSearchTool);

        var targets = new Dictionary<Type, object>
        {
            [typeof(GmailTool)] = gmailTool,
            [typeof(CalendarTool)] = calendarTool,
            [typeof(DropboxTool)] = dropboxTool,
            [typeof(FileSystemTool)] = fileSystemTool,
            [typeof(HttpTool)] = httpTool,
            [typeof(WebSearchTool)] = webSearchTool,
        };

        var functions = new Dictionary<string, AIFunction>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in ToolMap.Definitions.Where(static item => item.IsAgentTool))
        {
            var method = definition.DeclaringType.GetMethod(
                definition.MethodName!,
                BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException(
                    $"Tool Map method '{definition.DeclaringType.Name}.{definition.MethodName}' does not exist.");
            var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? $"Invokes workflow alias {definition.Alias}.";
            functions.Add(
                definition.Alias,
                AIFunctionFactory.Create(
                    method,
                    targets[definition.DeclaringType],
                    definition.AgentFunctionName!,
                    description,
                    ToolJson.Options));
        }

        _functionsByAlias = functions;
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolDefinition> Definitions => ToolMap.Definitions;

    /// <inheritdoc />
    public AITool[] GetAgentTools(IEnumerable<string> enabledToolGroups)
    {
        var whitelist = NormalizeWhitelist(enabledToolGroups);
        return ToolMap.Definitions
            .Where(definition =>
                definition.IsAgentTool && whitelist.Contains(definition.ToolGroup))
            .Select(definition => (AITool)_functionsByAlias[definition.Alias])
            .ToArray();
    }

    /// <inheritdoc />
    public bool TryGetDefinition(string alias, out ToolDefinition? definition)
    {
        definition = ToolMap.Find(alias);
        return definition is not null;
    }

    /// <inheritdoc />
    public async Task<string> InvokeAsync(
        string alias,
        JsonElement arguments,
        IEnumerable<string> enabledToolGroups,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        var definition = ToolMap.Find(alias)
            ?? throw new WorkflowValidationException($"Unknown Tool Map alias '{alias}'.");
        if (!definition.IsAgentTool)
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

                suppliedArguments[property.Name] = property.Value.Clone();
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

        var result = await _functionsByAlias[definition.Alias]
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

    private static HashSet<string> NormalizeWhitelist(
        IEnumerable<string> enabledToolGroups)
    {
        ArgumentNullException.ThrowIfNull(enabledToolGroups);
        var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in enabledToolGroups)
        {
            if (string.IsNullOrWhiteSpace(group) || !ToolGroups.All.Contains(group))
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
}
