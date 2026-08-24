using System.Reflection;
using System.Text;
using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Authoritative immutable catalog snapshot discovered from compiled assembly types.
/// </summary>
public sealed class ToolCatalog : IToolCatalog
{
    private static readonly Lazy<ToolCatalog> LazyDefault = new(
        () => FromAssembly(typeof(ToolCatalog).Assembly));

    private readonly Dictionary<string, ToolGroupDefinition> _groupsByName;
    private readonly Dictionary<string, ToolDefinition> _definitionsByAlias;

    private ToolCatalog(
        IReadOnlyList<ToolGroupDefinition> groups,
        IReadOnlyList<ToolDefinition> definitions)
    {
        Groups = groups;
        TaskWhitelistGroups = groups
            .Where(static group => group.IsTaskWhitelistGroup)
            .ToList();
        TaskWhitelistNames = new HashSet<string>(
            TaskWhitelistGroups.Select(static group => group.Name),
            StringComparer.OrdinalIgnoreCase);
        Definitions = definitions;

        _groupsByName = groups.ToDictionary(
            static group => group.Name,
            StringComparer.OrdinalIgnoreCase);
        _definitionsByAlias = definitions.ToDictionary(
            static def => def.Alias,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the default canonical production catalog snapshot.
    /// </summary>
    public static IToolCatalog Default => LazyDefault.Value;

    /// <inheritdoc />
    public IReadOnlyList<ToolGroupDefinition> Groups { get; }

    /// <inheritdoc />
    public IReadOnlyList<ToolGroupDefinition> TaskWhitelistGroups { get; }

    /// <inheritdoc />
    public IReadOnlySet<string> TaskWhitelistNames { get; }

    /// <inheritdoc />
    public IReadOnlyList<ToolDefinition> Definitions { get; }

    /// <inheritdoc />
    public ToolGroupDefinition? FindGroup(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _groupsByName.GetValueOrDefault(name);
    }

    /// <inheritdoc />
    public ToolDefinition? Find(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return null;
        }

        return _definitionsByAlias.GetValueOrDefault(alias);
    }

    /// <inheritdoc />
    public int GetGroupDisplayOrder(string name)
    {
        var group = FindGroup(name);
        return group?.DisplayOrder ?? int.MaxValue;
    }

    /// <summary>
    /// Discovers and validates attributed tools in the target assembly.
    /// </summary>
    /// <param name="assembly">The explicit assembly to scan.</param>
    /// <returns>A validated immutable catalog snapshot.</returns>
    public static ToolCatalog FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return FromTypes(assembly.GetTypes());
    }

    /// <summary>
    /// Discovers and validates attributed tools from an explicit set of types.
    /// </summary>
    /// <param name="types">The explicit candidate types.</param>
    /// <returns>A validated immutable catalog snapshot.</returns>
    public static ToolCatalog FromTypes(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var groupDefs = new List<ToolGroupDefinition>();
        var toolDefs = new List<ToolDefinition>();

        var discoveredGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveredDisplayOrders = new HashSet<int>();
        var discoveredAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveredAgentFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var candidateTypes = types
            .Where(static type => type.GetCustomAttribute<ToolGroupAttribute>() is not null)
            .ToList();

        foreach (var type in candidateTypes)
        {
            var groupAttr = type.GetCustomAttribute<ToolGroupAttribute>()!;

            // Rule 1: Blank names, display names, descriptions
            if (string.IsNullOrWhiteSpace(groupAttr.Name))
            {
                throw new InvalidOperationException($"Type '{type.FullName}' has a blank ToolGroup name.");
            }

            if (string.IsNullOrWhiteSpace(groupAttr.DisplayName))
            {
                throw new InvalidOperationException($"ToolGroup '{groupAttr.Name}' on type '{type.FullName}' has a blank display name.");
            }

            if (string.IsNullOrWhiteSpace(groupAttr.Description))
            {
                throw new InvalidOperationException($"ToolGroup '{groupAttr.Name}' on type '{type.FullName}' has a blank description.");
            }

            if (groupAttr.DisplayOrder <= 0)
            {
                throw new InvalidOperationException($"ToolGroup '{groupAttr.Name}' on type '{type.FullName}' has invalid DisplayOrder {groupAttr.DisplayOrder}. Display order must be positive.");
            }

            // Rule 8: Runtime class cannot claim synthetic 'ai' group
            if (string.Equals(groupAttr.Name, ToolGroups.Ai, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Type '{type.FullName}' cannot claim the reserved synthetic group '{ToolGroups.Ai}'.");
            }

            // Rule 4: Must be concrete class
            if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
            {
                throw new InvalidOperationException($"Tool group '{groupAttr.Name}' declaring type '{type.FullName}' must be a non-abstract, non-generic concrete class.");
            }

            // Rule 2 & 3: Duplicate group name or display order
            if (!discoveredGroupNames.Add(groupAttr.Name))
            {
                throw new InvalidOperationException($"Duplicate tool group name '{groupAttr.Name}' declared on type '{type.FullName}'.");
            }

            if (!discoveredDisplayOrders.Add(groupAttr.DisplayOrder))
            {
                throw new InvalidOperationException($"Duplicate tool group display order {groupAttr.DisplayOrder} declared for group '{groupAttr.Name}'.");
            }

            // Rule 9: ServiceContract check
            if (groupAttr.ServiceContract is not null)
            {
                if (!groupAttr.ServiceContract.IsInterface)
                {
                    throw new InvalidOperationException($"Tool group '{groupAttr.Name}' ServiceContract '{groupAttr.ServiceContract.FullName}' must be an interface.");
                }

                if (!groupAttr.ServiceContract.IsAssignableFrom(type))
                {
                    throw new InvalidOperationException($"Type '{type.FullName}' does not implement specified ServiceContract '{groupAttr.ServiceContract.FullName}'.");
                }
            }

            var groupDef = new ToolGroupDefinition(
                groupAttr.Name,
                groupAttr.DisplayName,
                groupAttr.Description,
                groupAttr.IsTaskWhitelistGroup,
                groupAttr.DisplayOrder);

            groupDefs.Add(groupDef);

            // Discover methods
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(static m => m.GetCustomAttribute<ToolAttribute>() is not null)
                .ToList();

            var groupMethodOrders = new HashSet<int>();

            foreach (var method in methods)
            {
                var toolAttr = method.GetCustomAttribute<ToolAttribute>()!;

                if (!Enum.IsDefined(toolAttr.RequiredCapability))
                {
                    throw new InvalidOperationException(
                        $"Tool '{toolAttr.Alias}' references unknown task capability '{toolAttr.RequiredCapability}'.");
                }

                // Rule 5: Non-public, static, generic, special-name method
                if (method.IsStatic || method.IsGenericMethod || method.IsSpecialName)
                {
                    throw new InvalidOperationException($"Tool method '{method.Name}' on type '{type.FullName}' must be a public non-static, non-generic instance method.");
                }

                if (method.DeclaringType != type)
                {
                    throw new InvalidOperationException($"Tool method '{method.Name}' must be declared directly on type '{type.FullName}'.");
                }

                // Rule 1: Blank alias, description, order <= 0
                if (string.IsNullOrWhiteSpace(toolAttr.Alias))
                {
                    throw new InvalidOperationException($"Tool method '{method.Name}' on type '{type.FullName}' has a blank alias.");
                }

                if (string.IsNullOrWhiteSpace(toolAttr.Description))
                {
                    throw new InvalidOperationException($"Tool '{toolAttr.Alias}' on method '{method.Name}' has a blank description.");
                }

                if (toolAttr.Order <= 0)
                {
                    throw new InvalidOperationException($"Tool '{toolAttr.Alias}' has invalid order {toolAttr.Order}. Order must be positive.");
                }

                // Check double DescriptionAttribute on same method
                if (method.GetCustomAttributes<System.ComponentModel.DescriptionAttribute>().Count() > 1)
                {
                    throw new InvalidOperationException($"Tool method '{method.Name}' has duplicate DescriptionAttribute.");
                }

                // Rule 6: Alias prefix must match group name
                var expectedPrefix = groupAttr.Name + ".";
                if (!toolAttr.Alias.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Tool alias '{toolAttr.Alias}' prefix does not match owning group name '{groupAttr.Name}'.");
                }

                // Rule 2: Duplicate alias or group-local method order
                if (!discoveredAliases.Add(toolAttr.Alias))
                {
                    throw new InvalidOperationException($"Duplicate tool alias '{toolAttr.Alias}' discovered on method '{method.Name}'.");
                }

                if (!groupMethodOrders.Add(toolAttr.Order))
                {
                    throw new InvalidOperationException($"Duplicate method order {toolAttr.Order} in group '{groupAttr.Name}' for tool '{toolAttr.Alias}'.");
                }

                // Derive agent function name
                var agentFunctionName = toolAttr.AgentFunctionName;
                if (string.IsNullOrWhiteSpace(agentFunctionName))
                {
                    agentFunctionName = DeriveAgentFunctionName(toolAttr.Alias);
                }

                if (!discoveredAgentFunctions.Add(agentFunctionName))
                {
                    throw new InvalidOperationException($"Duplicate agent function name '{agentFunctionName}' for tool alias '{toolAttr.Alias}'.");
                }

                // Validate parameters
                var parameters = method.GetParameters();
                var argDefs = new List<ToolArgumentDefinition>();
                var localPathAttrs = method.GetCustomAttributes<ToolLocalPathAttribute>().ToList();

                ParameterInfo? cancellationTokenParam = null;
                var nonCancellationParams = new List<ParameterInfo>();

                for (int i = 0; i < parameters.Length; i++)
                {
                    var param = parameters[i];
                    if (param.ParameterType == typeof(CancellationToken))
                    {
                        // Rule 12: CancellationToken validation
                        if (cancellationTokenParam is not null)
                        {
                            throw new InvalidOperationException($"Method '{method.Name}' has multiple CancellationToken parameters.");
                        }

                        if (i != parameters.Length - 1)
                        {
                            throw new InvalidOperationException($"CancellationToken parameter on method '{method.Name}' must be the final parameter.");
                        }

                        cancellationTokenParam = param;
                    }
                    else
                    {
                        nonCancellationParams.Add(param);
                    }
                }

                var paramNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var param in nonCancellationParams)
                {
                    // Rule 11: Missing or duplicate parameter attribute
                    var argAttrs = param.GetCustomAttributes<ToolArgumentAttribute>().ToList();
                    if (argAttrs.Count == 0)
                    {
                        throw new InvalidOperationException($"Parameter '{param.Name}' on method '{method.Name}' is missing [ToolArgument] attribute.");
                    }

                    if (argAttrs.Count > 1)
                    {
                        throw new InvalidOperationException($"Parameter '{param.Name}' on method '{method.Name}' has multiple [ToolArgument] attributes.");
                    }

                    if (param.GetCustomAttributes<System.ComponentModel.DescriptionAttribute>().Count() > 1)
                    {
                        throw new InvalidOperationException($"Parameter '{param.Name}' on method '{method.Name}' has duplicate DescriptionAttribute.");
                    }

                    var argAttr = argAttrs[0];

                    if (string.IsNullOrWhiteSpace(argAttr.Description))
                    {
                        throw new InvalidOperationException($"Parameter '{param.Name}' on tool '{toolAttr.Alias}' has a blank description.");
                    }

                    var paramName = param.Name!;
                    paramNames.Add(paramName);

                    // Requiredness check
                    bool isRequired = !param.IsOptional && param.DefaultValue == DBNull.Value;
                    if (argAttr.IsRequired.HasValue)
                    {
                        if (isRequired && !argAttr.IsRequired.Value)
                        {
                            throw new InvalidOperationException($"Parameter '{paramName}' on tool '{toolAttr.Alias}' is required by CLR and cannot be marked optional by attribute.");
                        }

                        isRequired = argAttr.IsRequired.Value;
                    }

                    // Enum allowed values
                    IReadOnlyList<string> allowedValues = argAttr.AllowedValues ?? [];
                    var paramType = Nullable.GetUnderlyingType(param.ParameterType) ?? param.ParameterType;

                    if (argAttr.Kind == WorkflowArgumentKind.Enumeration)
                    {
                        if (paramType.IsEnum)
                        {
                            var enumNames = Enum.GetNames(paramType);
                            if (allowedValues.Count == 0)
                            {
                                allowedValues = enumNames;
                            }
                        }
                        else if (allowedValues.Count == 0)
                        {
                            throw new InvalidOperationException($"Enumeration argument '{paramName}' on tool '{toolAttr.Alias}' must specify AllowedValues or be an enum type.");
                        }
                    }

                    bool supportsPlaceholders = argAttr.SupportsPlaceholders ?? (argAttr.Kind == WorkflowArgumentKind.String || argAttr.Kind == WorkflowArgumentKind.Enumeration);

                    var defaultValue = CreateDefaultValue(param);
                    var argumentDefinition = new ToolArgumentDefinition(
                        paramName,
                        argAttr.Kind,
                        isRequired,
                        argAttr.Description)
                    {
                        Example = argAttr.Example,
                        AllowedValues = allowedValues,
                        SupportsPlaceholders = supportsPlaceholders,
                        IsLongText = argAttr.IsLongText,
                        IsExcludedFromWorkflowTextEditor = argAttr.IsExcludedFromWorkflowTextEditor,
                        ValueType = paramType,
                        AllowsNull = Nullable.GetUnderlyingType(param.ParameterType) is not null || !param.ParameterType.IsValueType,
                        HasDefaultValue = defaultValue is not null,
                        DefaultValue = defaultValue,
                        Minimum = double.IsNaN(argAttr.Minimum) ? null : argAttr.Minimum,
                        Maximum = double.IsNaN(argAttr.Maximum) ? null : argAttr.Maximum,
                        MaximumLength = argAttr.MaximumLength > 0 ? argAttr.MaximumLength : null,
                        EditorHint = argAttr.EditorHint,
                    };
                    ValidateArgumentDefinition(toolAttr.Alias, argumentDefinition);
                    argDefs.Add(argumentDefinition);
                }

                // Rule 15: Local path attribute validation
                var localPathDefs = new List<LocalPathArgumentDefinition>();
                foreach (var pathAttr in localPathAttrs)
                {
                    if (string.IsNullOrWhiteSpace(pathAttr.ArgumentName) || !paramNames.Contains(pathAttr.ArgumentName))
                    {
                        throw new InvalidOperationException($"ToolLocalPathAttribute on '{toolAttr.Alias}' references unknown argument '{pathAttr.ArgumentName}'.");
                    }

                    if (!string.IsNullOrWhiteSpace(pathAttr.ValueArgumentName) && !paramNames.Contains(pathAttr.ValueArgumentName))
                    {
                        throw new InvalidOperationException($"ToolLocalPathAttribute on '{toolAttr.Alias}' references unknown ValueArgumentName '{pathAttr.ValueArgumentName}'.");
                    }
                    if ((pathAttr.PickerMode == LocalPathPickerMode.SelectFolder && pathAttr.TargetKind == LocalPathTargetKind.File) ||
                        (pathAttr.PickerMode is LocalPathPickerMode.OpenFile or LocalPathPickerMode.SaveFile && pathAttr.TargetKind == LocalPathTargetKind.Directory))
                    {
                        throw new InvalidOperationException($"ToolLocalPathAttribute on '{toolAttr.Alias}' uses picker '{pathAttr.PickerMode}' with incompatible target kind '{pathAttr.TargetKind}'.");
                    }
                    if (pathAttr.PickerMode == LocalPathPickerMode.SaveFile && pathAttr.RequiredAccess != FileSystemAccessMode.ReadWrite)
                    {
                        throw new InvalidOperationException($"ToolLocalPathAttribute on '{toolAttr.Alias}' uses a save-file picker without read-write access.");
                    }

                    localPathDefs.Add(new LocalPathArgumentDefinition(
                        pathAttr.ArgumentName,
                        pathAttr.TargetKind,
                        pathAttr.RequiredAccess,
                        pathAttr.Derivation,
                        pathAttr.ValueArgumentName)
                    {
                        DefaultValue = pathAttr.DefaultValue,
                        PickerMode = pathAttr.PickerMode,
                    });
                }

                var argNames = argDefs.Select(static a => a.Name).ToList();
                var optionalArgNames = argDefs.Where(static a => !a.IsRequired).Select(static a => a.Name).ToList();

                var toolDef = new ToolDefinition(
                    toolAttr.Alias,
                    agentFunctionName,
                    groupAttr.Name,
                    type,
                    method.Name,
                    argNames,
                    toolAttr.RequiredCapability == TaskCapability.None
                        ? null
                        : toolAttr.RequiredCapability,
                    IsAiStep: false)
                {
                    OptionalArguments = optionalArgNames,
                    Description = toolAttr.Description,
                    ArgumentDefinitions = argDefs,
                    LocalPathArguments = localPathDefs,
                    Order = toolAttr.Order,
                    Activation = groupAttr.Activation,
                    Method = method,
                };

                toolDefs.Add(toolDef);
            }

            // Rule 7: Whitelist group must have at least one method
            if (groupAttr.IsTaskWhitelistGroup && groupMethodOrders.Count == 0)
            {
                throw new InvalidOperationException($"Task-whitelist group '{groupAttr.Name}' on type '{type.FullName}' has no invocable tool methods.");
            }
        }

        // Add explicit AI step definitions
        AddBuiltInAiSteps(groupDefs, toolDefs, discoveredAliases);
        AddBuiltInFlowSteps(groupDefs, toolDefs, discoveredAliases);

        // Sort groups by DisplayOrder
        groupDefs.Sort(static (a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));

        // Sort definitions: group display order first, then method order (or explicit order for AI)
        var groupOrderLookup = groupDefs.ToDictionary(static g => g.Name, static g => g.DisplayOrder, StringComparer.OrdinalIgnoreCase);

        toolDefs.Sort((a, b) =>
        {
            int groupOrderA = groupOrderLookup.GetValueOrDefault(a.ToolGroup, 2000);
            int groupOrderB = groupOrderLookup.GetValueOrDefault(b.ToolGroup, 2000);
            if (groupOrderA != groupOrderB)
            {
                return groupOrderA.CompareTo(groupOrderB);
            }

            return a.Order.CompareTo(b.Order);
        });

        return new ToolCatalog(groupDefs, toolDefs);
    }

    private static void AddBuiltInAiSteps(
        List<ToolGroupDefinition> groupDefs,
        List<ToolDefinition> toolDefs,
        HashSet<string> discoveredAliases)
    {
        var aiGroup = new ToolGroupDefinition(
            ToolGroups.Ai,
            "Built-in AI",
            "Run built-in AI workflow steps that do not require a task tool-group permission.",
            IsTaskWhitelistGroup: false,
            DisplayOrder: 2000);

        groupDefs.Add(aiGroup);

        int aiOrder = 100;

        void AddAiStep(
            string alias,
            Type declaringType,
            string description,
            IReadOnlyList<ToolArgumentDefinition> arguments)
        {
            if (!discoveredAliases.Add(alias))
            {
                throw new InvalidOperationException($"Duplicate alias '{alias}' in AI steps.");
            }

            foreach (var argument in arguments)
            {
                ValidateArgumentDefinition(alias, argument);
            }

            var argNames = arguments.Select(static a => a.Name).ToList();
            var optionalArgNames = arguments.Where(static a => !a.IsRequired).Select(static a => a.Name).ToList();

            toolDefs.Add(new ToolDefinition(
                alias,
                AgentFunctionName: null,
                ToolGroups.Ai,
                declaringType,
                MethodName: null,
                argNames,
                RequiredCapability: null,
                IsAiStep: true)
            {
                OptionalArguments = optionalArgNames,
                Description = description,
                ArgumentDefinitions = arguments,
                Order = aiOrder++,
                Activation = ToolActivationKind.Singleton,
            });
        }

        AddAiStep(
            "ai.summarize",
            typeof(SummarizeTextSkill),
            "Summarizes the supplied text, or the previous step output when text is omitted.",
            [AiTextArg()]);

        AddAiStep(
            "ai.extractActionItems",
            typeof(ExtractActionItemsSkill),
            "Extracts action items from the supplied text, or from the previous step output when text is omitted.",
            [AiTextArg()]);

        AddAiStep(
            "ai.classifyEmail",
            typeof(ClassifyEmailSkill),
            "Classifies the supplied email text, or the previous step output when text is omitted.",
            [AiTextArg()]);

        AddAiStep(
            ToolMap.PromptAlias,
            typeof(MyLocalAgentService),
            "Sends a workflow-authored prompt to the local model and returns its response.",
            [
                new ToolArgumentDefinition(
                    "prompt",
                    WorkflowArgumentKind.String,
                    IsRequired: true,
                    "Prompt text sent to the local model; placeholders are substituted before the call.")
                {
                    Example = "Summarize the workflow context in five bullet points.",
                    SupportsPlaceholders = true,
                    IsLongText = true,
                    ValueType = typeof(string),
                },
                new ToolArgumentDefinition(
                    "includeContext",
                    WorkflowArgumentKind.Boolean,
                    IsRequired: false,
                    "Whether the complete accumulated workflow context is appended to the prompt; omitted means false.")
                {
                    Example = "true",
                    SupportsPlaceholders = false,
                    ValueType = typeof(bool),
                    AllowsNull = false,
                    HasDefaultValue = true,
                    DefaultValue = JsonSerializer.SerializeToElement(false),
                },
                new ToolArgumentDefinition(
                    "timeout",
                    WorkflowArgumentKind.Integer,
                    IsRequired: false,
                    "Optional per-step timeout in minutes from 1 to 999; omitted uses the provider default.")
                {
                    Example = "20",
                    SupportsPlaceholders = false,
                    ValueType = typeof(int),
                    AllowsNull = false,
                    Minimum = 1,
                    Maximum = 999,
                },
                new ToolArgumentDefinition(
                    "thinking",
                    WorkflowArgumentKind.Boolean,
                    IsRequired: false,
                    "Optional per-step reasoning control; omitted uses the task or global generation setting.")
                {
                    Example = "false",
                    SupportsPlaceholders = false,
                    ValueType = typeof(bool),
                    AllowsNull = false,
                },
            ]);
    }

    private static ToolArgumentDefinition AiTextArg() =>
        new(
            "text",
            WorkflowArgumentKind.String,
            IsRequired: false,
            "Text content to summarize/extract/classify; omitted means previous step output.")
        {
            Example = "The quarterly financial results were positive across all regions.",
            SupportsPlaceholders = true,
            ValueType = typeof(string),
            AllowsNull = true,
        };

    private static System.Text.Json.JsonElement? CreateDefaultValue(ParameterInfo parameter)
    {
        if (!parameter.HasDefaultValue || parameter.DefaultValue is null || parameter.DefaultValue == DBNull.Value)
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.SerializeToElement(parameter.DefaultValue, parameter.ParameterType, ScheduledTaskSerializer.Options);
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
        {
            throw new InvalidOperationException($"Parameter '{parameter.Name}' has an unsupported default value.", exception);
        }
    }

    private static void AddBuiltInFlowSteps(
        List<ToolGroupDefinition> groupDefs,
        List<ToolDefinition> toolDefs,
        HashSet<string> discoveredAliases)
    {
        const string alias = "flow.decision";
        if (!discoveredAliases.Add(alias))
        {
            throw new InvalidOperationException($"Duplicate alias '{alias}' in flow steps.");
        }

        groupDefs.Add(new ToolGroupDefinition(
            ToolGroups.Flow,
            "Flow control",
            "Route workflow execution through deterministic conditions.",
            IsTaskWhitelistGroup: false,
            DisplayOrder: 1900));

        var arguments = new ToolArgumentDefinition[]
        {
            new("source", WorkflowArgumentKind.String, true, "Text or structured output to test.")
            {
                Example = "{{previous.output}}",
                ValueType = typeof(string),
            },
            new("path", WorkflowArgumentKind.String, false, "Optional dot-and-index path within a JSON source.")
            {
                Example = "items[0].category",
                ValueType = typeof(string),
            },
            new("operator", WorkflowArgumentKind.Enumeration, true, "Deterministic comparison operator.")
            {
                Example = "equals",
                ValueType = typeof(string),
                AllowedValues =
                [
                    "equals", "contains", "startsWith", "endsWith", "matches", "exists",
                    "isNull", "isEmpty", "isNotEmpty", "lessThan", "lessThanOrEqual",
                    "greaterThan", "greaterThanOrEqual",
                ],
                SupportsPlaceholders = false,
            },
            new("value", WorkflowArgumentKind.Object, false, "Comparison value required by binary operators.")
            {
                Example = "\"urgent\"",
                ValueType = typeof(JsonElement),
                AllowsNull = true,
                SupportsPlaceholders = false,
                AllowsScalarJson = true,
                EditorHint = WorkflowArgumentEditorHint.StructuredJson,
            },
            new("caseSensitive", WorkflowArgumentKind.Boolean, false, "Whether string comparisons preserve case.")
            {
                Example = "false",
                ValueType = typeof(bool),
                SupportsPlaceholders = false,
                HasDefaultValue = true,
                DefaultValue = JsonSerializer.SerializeToElement(false),
            },
            new("negate", WorkflowArgumentKind.Boolean, false, "Whether to invert the evaluated condition.")
            {
                Example = "false",
                ValueType = typeof(bool),
                SupportsPlaceholders = false,
                HasDefaultValue = true,
                DefaultValue = JsonSerializer.SerializeToElement(false),
            },
            new("target", WorkflowArgumentKind.String, true, "Step id selected when the condition matches, or @end.")
            {
                Example = "matchedStep",
                ValueType = typeof(string),
                SupportsPlaceholders = false,
            },
            new("fallback", WorkflowArgumentKind.String, false, "Step id selected when the condition cannot be evaluated, or @end.")
            {
                Example = "manualReview",
                ValueType = typeof(string),
                SupportsPlaceholders = false,
            },
        };
        foreach (var argument in arguments)
        {
            ValidateArgumentDefinition(alias, argument);
        }

        toolDefs.Add(new ToolDefinition(
            alias,
            AgentFunctionName: null,
            ToolGroups.Flow,
            typeof(WorkflowDecisionOperation),
            MethodName: null,
            arguments.Select(static argument => argument.Name).ToArray(),
            RequiredCapability: null,
            IsAiStep: false)
        {
            IsControlFlowStep = true,
            OptionalArguments = arguments.Where(static argument => !argument.IsRequired).Select(static argument => argument.Name).ToArray(),
            Description = "Routes execution to a target when a deterministic condition matches, with an optional fallback for indeterminate values.",
            ArgumentDefinitions = arguments,
            Order = 100,
        });
    }

    private static void ValidateArgumentDefinition(string alias, ToolArgumentDefinition argument)
    {
        var type = argument.ValueType;
        var compatible = argument.Kind switch
        {
            WorkflowArgumentKind.String => type == typeof(string) || type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(Uri),
            WorkflowArgumentKind.Boolean => type == typeof(bool),
            WorkflowArgumentKind.Integer => type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong),
            WorkflowArgumentKind.Number => type == typeof(float) || type == typeof(double) || type == typeof(decimal),
            WorkflowArgumentKind.Enumeration => type.IsEnum || type == typeof(string),
            WorkflowArgumentKind.Object => type == typeof(JsonElement) || type.IsArray || (!type.IsPrimitive && type != typeof(string) && !type.IsEnum),
            _ => false,
        };
        if (!compatible)
        {
            throw new InvalidOperationException($"Argument '{argument.Name}' on tool '{alias}' has incompatible kind '{argument.Kind}' and CLR type '{type.Name}'.");
        }

        if (argument.Minimum is { } minimum && !double.IsFinite(minimum) || argument.Maximum is { } maximum && !double.IsFinite(maximum))
        {
            throw new InvalidOperationException($"Argument '{argument.Name}' on tool '{alias}' has a non-finite numeric bound.");
        }
        if (argument.Minimum is { } lower && argument.Maximum is { } upper && lower > upper)
        {
            throw new InvalidOperationException($"Argument '{argument.Name}' on tool '{alias}' has a minimum greater than its maximum.");
        }
        if ((argument.Minimum is not null || argument.Maximum is not null) && argument.Kind is not WorkflowArgumentKind.Integer and not WorkflowArgumentKind.Number)
        {
            throw new InvalidOperationException($"Argument '{argument.Name}' on tool '{alias}' defines numeric bounds for a non-numeric kind.");
        }
        if (argument.MaximumLength is <= 0)
        {
            throw new InvalidOperationException($"Argument '{argument.Name}' on tool '{alias}' has a non-positive maximum length.");
        }
        if (argument.MaximumLength is not null && argument.Kind is not WorkflowArgumentKind.String and not WorkflowArgumentKind.Enumeration)
        {
            throw new InvalidOperationException($"Argument '{argument.Name}' on tool '{alias}' defines a maximum length for a non-string kind.");
        }
        if (argument.EditorHint == WorkflowArgumentEditorHint.StructuredJson && argument.Kind != WorkflowArgumentKind.Object || argument.EditorHint == WorkflowArgumentEditorHint.KnowledgeTags && argument.Kind != WorkflowArgumentKind.Object)
        {
            throw new InvalidOperationException($"Argument '{argument.Name}' on tool '{alias}' has an incompatible editor hint.");
        }
        if (argument.DefaultValue is { } defaultValue && !IsDefaultCompatible(defaultValue, argument))
        {
            throw new InvalidOperationException($"Argument '{argument.Name}' on tool '{alias}' has an incompatible reflected default value.");
        }
    }

    private static bool IsDefaultCompatible(JsonElement value, ToolArgumentDefinition argument)
    {
        var compatible = argument.Kind switch
        {
            WorkflowArgumentKind.String or WorkflowArgumentKind.Enumeration => value.ValueKind == JsonValueKind.String,
            WorkflowArgumentKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            WorkflowArgumentKind.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            WorkflowArgumentKind.Number => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var finite) && double.IsFinite(finite),
            WorkflowArgumentKind.Object => value.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
            _ => false,
        };
        if (!compatible) return false;
        if (argument.AllowedValues.Count > 0 && value.ValueKind == JsonValueKind.String && !argument.AllowedValues.Contains(value.GetString()!, StringComparer.Ordinal)) return false;
        if (argument.MaximumLength is { } maximumLength && value.ValueKind == JsonValueKind.String && value.GetString()!.Length > maximumLength) return false;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && (argument.Minimum is { } minimum && number < minimum || argument.Maximum is { } maximum && number > maximum)) return false;
        return true;
    }

    /// <summary>
    /// Derives the default lower snake-case agent function name from a workflow alias.
    /// </summary>
    /// <param name="alias">The workflow alias.</param>
    /// <returns>The lower snake-case function name.</returns>
    public static string DeriveAgentFunctionName(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (int i = 0; i < alias.Length; i++)
        {
            char c = alias[i];
            if (c == '.')
            {
                builder.Append('_');
            }
            else if (char.IsUpper(c))
            {
                if (i > 0 && alias[i - 1] != '.' && alias[i - 1] != '_' && !char.IsUpper(alias[i - 1]))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
