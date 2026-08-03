using System.Text.RegularExpressions;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Expands model-information placeholders in one non-recursive pass.
/// </summary>
public static partial class ModelPromptPlaceholderResolver
{
    /// <summary>
    /// Resolves model placeholders while preserving every unrelated marker exactly.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="context">The immutable active-model snapshot.</param>
    /// <returns>The text with supported model placeholders expanded.</returns>
    public static string Resolve(
        string text,
        ModelPromptPlaceholderContext context)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);

        return PlaceholderPattern().Replace(
            text,
            match => TryResolve(
                match.Groups[1].Value.Trim(),
                context,
                out var value)
                    ? value
                    : match.Value);
    }

    /// <summary>
    /// Determines whether a placeholder name belongs to the model-information contract.
    /// </summary>
    public static bool IsSupported(string placeholder)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        return placeholder.Equals("ModelName", StringComparison.OrdinalIgnoreCase) ||
            placeholder.Equals("ModelSettings", StringComparison.OrdinalIgnoreCase) ||
            placeholder.Equals("ModelContextSize", StringComparison.OrdinalIgnoreCase) ||
            placeholder.Equals("ModelInfo", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to resolve one trimmed placeholder body.
    /// </summary>
    public static bool TryResolve(
        string placeholder,
        ModelPromptPlaceholderContext context,
        out string value)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        ArgumentNullException.ThrowIfNull(context);

        if (placeholder.Equals("ModelName", StringComparison.OrdinalIgnoreCase))
        {
            value = context.ModelName;
            return true;
        }

        if (placeholder.Equals("ModelSettings", StringComparison.OrdinalIgnoreCase))
        {
            value = context.ModelSettings;
            return true;
        }

        if (placeholder.Equals("ModelContextSize", StringComparison.OrdinalIgnoreCase))
        {
            value = context.ModelContextSize;
            return true;
        }

        if (placeholder.Equals("ModelInfo", StringComparison.OrdinalIgnoreCase))
        {
            value = context.ModelInfo;
            return true;
        }

        value = string.Empty;
        return false;
    }

    [GeneratedRegex(
        @"\{\{\s*([^{}]+?)\s*\}\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
