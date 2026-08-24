using System.Text.Json;
using System.Text.RegularExpressions;

namespace OD.MyLocalAgent.Core;

/// <summary>Defines the closed set of deterministic Stage 29 decision operators.</summary>
public enum WorkflowDecisionOperator
{
    /// <summary>Compares scalar values for equality.</summary>
    Equals,
    /// <summary>Tests whether text contains a value.</summary>
    Contains,
    /// <summary>Tests whether text starts with a value.</summary>
    StartsWith,
    /// <summary>Tests whether text ends with a value.</summary>
    EndsWith,
    /// <summary>Tests text with a regular expression.</summary>
    Matches,
    /// <summary>Tests whether a selected value exists.</summary>
    Exists,
    /// <summary>Tests whether a selected value is JSON null.</summary>
    IsNull,
    /// <summary>Tests whether a string, array, or object is empty.</summary>
    IsEmpty,
    /// <summary>Tests whether a string, array, or object is not empty.</summary>
    IsNotEmpty,
    /// <summary>Compares numbers.</summary>
    LessThan,
    /// <summary>Compares numbers.</summary>
    LessThanOrEqual,
    /// <summary>Compares numbers.</summary>
    GreaterThan,
    /// <summary>Compares numbers.</summary>
    GreaterThanOrEqual,
}

/// <summary>Represents normalized arguments of a <c>flow.decision</c> step.</summary>
public sealed partial record WorkflowDecisionDefinition(
    string Source,
    string? Path,
    WorkflowDecisionOperator Operator,
    JsonElement? Value,
    bool CaseSensitive,
    bool Negate,
    string Target,
    string? Fallback)
{
    /// <summary>Parses and validates one decision argument object.</summary>
    public static WorkflowDecisionDefinition Parse(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            throw new WorkflowValidationException("Workflow alias 'flow.decision' requires an argument object.");
        }

        var source = ReadRequiredString(args, "source");
        var path = ReadOptionalString(args, "path");
        var operatorText = ReadRequiredString(args, "operator");
        if (!Enum.TryParse<WorkflowDecisionOperator>(operatorText, true, out var decisionOperator))
        {
            throw new WorkflowValidationException($"Workflow decision operator '{operatorText}' is unsupported.");
        }

        if (path is not null && !PathPattern().IsMatch(path))
        {
            throw new WorkflowValidationException($"Workflow decision path '{path}' is invalid.");
        }

        var requiresValue = decisionOperator is WorkflowDecisionOperator.Equals or
            WorkflowDecisionOperator.Contains or WorkflowDecisionOperator.StartsWith or
            WorkflowDecisionOperator.EndsWith or WorkflowDecisionOperator.Matches or
            WorkflowDecisionOperator.LessThan or WorkflowDecisionOperator.LessThanOrEqual or
            WorkflowDecisionOperator.GreaterThan or WorkflowDecisionOperator.GreaterThanOrEqual;
        var hasValue = args.TryGetProperty("value", out var value);
        if (requiresValue && !hasValue)
        {
            throw new WorkflowValidationException($"Workflow decision operator '{operatorText}' requires argument 'value'.");
        }

        if (!requiresValue && hasValue)
        {
            throw new WorkflowValidationException($"Workflow decision operator '{operatorText}' does not accept argument 'value'.");
        }

        if (decisionOperator is WorkflowDecisionOperator.Contains or WorkflowDecisionOperator.StartsWith or
            WorkflowDecisionOperator.EndsWith or WorkflowDecisionOperator.Matches &&
            value.ValueKind != JsonValueKind.String)
        {
            throw new WorkflowValidationException($"Workflow decision operator '{operatorText}' requires a string value.");
        }

        if (decisionOperator == WorkflowDecisionOperator.Matches)
        {
            try
            {
                _ = new Regex(value.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            }
            catch (ArgumentException exception)
            {
                throw new WorkflowValidationException($"Workflow decision regular expression is invalid: {exception.Message}");
            }
        }

        return new WorkflowDecisionDefinition(
            source,
            path,
            decisionOperator,
            hasValue ? value.Clone() : null,
            ReadOptionalBoolean(args, "caseSensitive") ?? false,
            ReadOptionalBoolean(args, "negate") ?? false,
            ReadRequiredString(args, "target"),
            ReadOptionalString(args, "fallback"));
    }

    private static string ReadRequiredString(JsonElement args, string name) =>
        ReadOptionalString(args, name) is { Length: > 0 } value
            ? value
            : throw new WorkflowValidationException($"Workflow alias 'flow.decision' requires non-empty string argument '{name}'.");

    private static string? ReadOptionalString(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new WorkflowValidationException($"Workflow decision argument '{name}' must be a string.");
    }

    private static bool? ReadOptionalBoolean(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new WorkflowValidationException($"Workflow decision argument '{name}' must be a boolean.");
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_-]*(?:(?:\.[A-Za-z_][A-Za-z0-9_-]*)|(?:\[[0-9]+\]))*$", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
}
