using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>Evaluates normalized deterministic workflow decisions.</summary>
internal static class WorkflowDecisionEvaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public static string Evaluate(JsonElement resolvedArguments)
    {
        var decision = WorkflowDecisionDefinition.Parse(resolvedArguments);
        try
        {
            var selected = SelectSource(decision.Source, decision.Path);
            var matched = EvaluateOperator(selected, decision);
            matched = decision.Negate ? !matched : matched;
            return JsonSerializer.Serialize(new
            {
                outcome = matched ? "matched" : "notMatched",
                matched,
                target = matched ? decision.Target : (string?)null,
            });
        }
        catch (Exception exception) when (IsEvaluationFailure(exception))
        {
            if (decision.Fallback is null)
            {
                throw new WorkflowDecisionEvaluationException("The workflow decision could not be evaluated.", exception);
            }

            return JsonSerializer.Serialize(new
            {
                outcome = "fallback",
                matched = (bool?)null,
                target = decision.Fallback,
                reason = Classify(exception),
            });
        }
    }

    public static string CreateFallback(JsonElement arguments, Exception exception)
    {
        var decision = WorkflowDecisionDefinition.Parse(arguments);
        if (decision.Fallback is null)
        {
            throw new WorkflowDecisionEvaluationException("The workflow decision source could not be resolved.", exception);
        }

        return JsonSerializer.Serialize(new
        {
            outcome = "fallback",
            matched = (bool?)null,
            target = decision.Fallback,
            reason = "sourceUnavailable",
        });
    }

    private static SelectedValue SelectSource(string source, string? path)
    {
        if (path is null)
        {
            try
            {
                using var document = JsonDocument.Parse(source);
                return new SelectedValue(true, document.RootElement.Clone());
            }
            catch (JsonException)
            {
                return new SelectedValue(true, JsonSerializer.SerializeToElement(source));
            }
        }

        using var sourceDocument = JsonDocument.Parse(source);
        var current = sourceDocument.RootElement;
        var position = 0;
        while (position < path.Length)
        {
            var nameStart = position;
            while (position < path.Length && path[position] is not '.' and not '[')
            {
                position++;
            }

            if (position > nameStart)
            {
                if (current.ValueKind != JsonValueKind.Object || !TryGetProperty(current, path[nameStart..position], out current))
                {
                    return new SelectedValue(false, default);
                }
            }

            if (position < path.Length && path[position] == '.')
            {
                position++;
                continue;
            }

            if (position < path.Length && path[position] == '[')
            {
                var end = path.IndexOf(']', position + 1);
                if (end < 0 || !int.TryParse(path[(position + 1)..end], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                    current.ValueKind != JsonValueKind.Array || index < 0 || index >= current.GetArrayLength())
                {
                    return new SelectedValue(false, default);
                }

                current = current[index];
                position = end + 1;
                if (position < path.Length && path[position] == '.')
                {
                    position++;
                }
            }
        }

        return new SelectedValue(true, current.Clone());
    }

    private static bool EvaluateOperator(SelectedValue selected, WorkflowDecisionDefinition decision)
    {
        if (decision.Operator == WorkflowDecisionOperator.Exists)
        {
            return selected.Exists;
        }

        if (!selected.Exists)
        {
            throw new InvalidOperationException("The selected path does not exist.");
        }

        var value = selected.Value;
        return decision.Operator switch
        {
            WorkflowDecisionOperator.IsNull => value.ValueKind == JsonValueKind.Null,
            WorkflowDecisionOperator.IsEmpty => IsEmpty(value),
            WorkflowDecisionOperator.IsNotEmpty => !IsEmpty(value),
            WorkflowDecisionOperator.Equals => EqualsValue(value, decision.Value!.Value, decision.CaseSensitive),
            WorkflowDecisionOperator.Contains => ReadText(value).Contains(ReadText(decision.Value!.Value), Comparison(decision.CaseSensitive)),
            WorkflowDecisionOperator.StartsWith => ReadText(value).StartsWith(ReadText(decision.Value!.Value), Comparison(decision.CaseSensitive)),
            WorkflowDecisionOperator.EndsWith => ReadText(value).EndsWith(ReadText(decision.Value!.Value), Comparison(decision.CaseSensitive)),
            WorkflowDecisionOperator.Matches => Regex.IsMatch(ReadText(value), ReadText(decision.Value!.Value), decision.CaseSensitive ? RegexOptions.CultureInvariant : RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout),
            WorkflowDecisionOperator.LessThan => ReadNumber(value) < ReadNumber(decision.Value!.Value),
            WorkflowDecisionOperator.LessThanOrEqual => ReadNumber(value) <= ReadNumber(decision.Value!.Value),
            WorkflowDecisionOperator.GreaterThan => ReadNumber(value) > ReadNumber(decision.Value!.Value),
            WorkflowDecisionOperator.GreaterThanOrEqual => ReadNumber(value) >= ReadNumber(decision.Value!.Value),
            _ => throw new InvalidOperationException("The decision operator is unsupported."),
        };
    }

    private static bool EqualsValue(JsonElement left, JsonElement right, bool caseSensitive)
    {
        if (left.ValueKind == JsonValueKind.String && right.ValueKind == JsonValueKind.String)
        {
            return string.Equals(left.GetString(), right.GetString(), Comparison(caseSensitive));
        }

        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number)
        {
            return ReadNumber(left) == ReadNumber(right);
        }

        return JsonElement.DeepEquals(left, right);
    }

    private static bool IsEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!.Length == 0,
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        _ => throw new InvalidOperationException("The selected value does not have an emptiness semantic."),
    };

    private static string ReadText(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()!
        : throw new InvalidOperationException("The selected value is not text.");

    private static decimal ReadNumber(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
        ? number
        : value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number)
            ? number
            : throw new InvalidOperationException("The selected value is not an invariant number.");

    private static StringComparison Comparison(bool caseSensitive) =>
        caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static bool TryGetProperty(JsonElement value, string name, out JsonElement result)
    {
        foreach (var property in value.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                result = property.Value;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static bool IsEvaluationFailure(Exception exception) => exception is JsonException or
        InvalidOperationException or FormatException or RegexMatchTimeoutException;

    private static string Classify(Exception exception) => exception switch
    {
        JsonException => "invalidJson",
        RegexMatchTimeoutException => "regexTimeout",
        _ => "incompatibleValue",
    };

    private readonly record struct SelectedValue(bool Exists, JsonElement Value);
}

/// <summary>Represents a deterministic decision that cannot be evaluated.</summary>
public sealed class WorkflowDecisionEvaluationException : Exception
{
    /// <summary>Initializes the exception.</summary>
    public WorkflowDecisionEvaluationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
