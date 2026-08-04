using System.Text.Json;
using System.Text.RegularExpressions;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Derives application-owned workflow requirements without executing or inspecting targets.
/// </summary>
public static partial class WorkflowRequirementAnalyzer
{
    /// <summary>
    /// Analyzes a workflow against the current task authorization and Safety configuration.
    /// </summary>
    /// <param name="workflow">The ordered workflow definitions.</param>
    /// <param name="currentToolGroups">The task's current tool groups.</param>
    /// <param name="currentAccess">The task's current absolute-directory rules.</param>
    /// <param name="safety">The current global Safety settings.</param>
    /// <returns>The deterministic requirement analysis.</returns>
    public static WorkflowRequirementAnalysis Analyze(
        IReadOnlyList<WorkflowStepDefinition> workflow,
        IEnumerable<string> currentToolGroups,
        IReadOnlyList<FileSystemAccessRule> currentAccess,
        SafetyOptions safety)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(currentToolGroups);
        ArgumentNullException.ThrowIfNull(currentAccess);
        ArgumentNullException.ThrowIfNull(safety);

        var currentGroups = new HashSet<string>(
            currentToolGroups,
            StringComparer.OrdinalIgnoreCase);
        var requiredGroups = workflow
            .Select(step => ToolMap.Find(step.Step))
            .Where(static definition => definition is { IsAiStep: false })
            .Select(static definition => definition!.ToolGroup)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingGroups = requiredGroups
            .Where(group => !currentGroups.Contains(group))
            .ToArray();
        var warnings = new List<string>();
        var candidates = new List<PathCandidate>();

        foreach (var step in workflow)
        {
            var definition = ToolMap.Find(step.Step);
            if (definition is null || step.Args.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var pathDefinition in definition.LocalPathArguments)
            {
                AnalyzePath(
                    step,
                    definition,
                    pathDefinition,
                    currentAccess,
                    candidates,
                    warnings);
            }

            if (string.Equals(
                    step.Step,
                    ToolMap.PromptAlias,
                    StringComparison.OrdinalIgnoreCase) &&
                step.Args.ValueKind == JsonValueKind.Object &&
                step.Args.TryGetProperty("includeContext", out var includeContext) &&
                includeContext.ValueKind == JsonValueKind.True)
            {
                warnings.Add(
                    "ai.prompt includes the accumulated workflow context; large prior outputs can exceed the configured model context.");
            }

            if (definition.LocalPathArguments.Count > 0)
            {
                warnings.Add(
                    $"{definition.Alias} accesses local filesystem content, which is authorization-controlled but not classified by Guardrails.");
            }
        }

        var pathRequirements = candidates
            .GroupBy(
                static candidate => candidate.DirectoryPath,
                StringComparer.OrdinalIgnoreCase)
            .Select(
                group =>
                {
                    var access = group.Any(
                        static item => item.RequiredAccess == FileSystemAccessMode.ReadWrite)
                            ? FileSystemAccessMode.ReadWrite
                            : FileSystemAccessMode.ReadOnly;
                    var path = group.Key;
                    var root = Path.GetPathRoot(path);
                    return new WorkflowPathRequirement(
                        path,
                        access,
                        group.Select(static item => item.Alias)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                        group.Any(static item => item.IsBroaderScope),
                        string.Equals(
                            Path.TrimEndingDirectorySeparator(path),
                            Path.TrimEndingDirectorySeparator(root ?? string.Empty),
                            StringComparison.OrdinalIgnoreCase),
                        path.StartsWith(@"\\", StringComparison.Ordinal));
                })
            .OrderBy(static requirement => requirement.DirectoryPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var safetyRequirements = workflow
            .Select(step => ToolMap.Find(step.Step))
            .Where(static definition => !string.IsNullOrWhiteSpace(definition?.SafetyFlag))
            .GroupBy(
                static definition => definition!.SafetyFlag!,
                StringComparer.OrdinalIgnoreCase)
            .Select(
                group => new WorkflowSafetyRequirement(
                    group.Key,
                    IsSafetyEnabled(group.Key, safety),
                    group.Select(static definition => definition!.Alias)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
            .OrderBy(static requirement => requirement.ConfigurationKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var requirement in safetyRequirements.Where(static item => !item.IsEnabled))
        {
            warnings.Add(
                $"{requirement.ConfigurationKey} is disabled; execution of {string.Join(", ", requirement.SourceAliases)} will fail until the user enables it in Settings.");
        }

        return new WorkflowRequirementAnalysis(
            requiredGroups,
            missingGroups,
            pathRequirements,
            safetyRequirements,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void AnalyzePath(
        WorkflowStepDefinition step,
        ToolDefinition tool,
        LocalPathArgumentDefinition definition,
        IReadOnlyList<FileSystemAccessRule> currentAccess,
        List<PathCandidate> candidates,
        List<string> warnings)
    {
        if (!TryGetStringArgument(step.Args, definition.ArgumentName, out var source))
        {
            warnings.Add(
                $"{tool.Alias}.{definition.ArgumentName} is not a literal string, so no filesystem grant was inferred.");
            return;
        }

        if (IsDynamic(source))
        {
            warnings.Add(
                $"{tool.Alias}.{definition.ArgumentName} contains a placeholder or unexpanded variable; runtime authorization remains authoritative.");
            return;
        }

        if (definition.Derivation ==
                LocalPathDerivationKind.OptionalDestinationOrSameDirectoryExtension &&
            TryGetStringArgument(
                step.Args,
                definition.ValueArgumentName!,
                out var optionalDestination) &&
            IsDynamic(optionalDestination))
        {
            warnings.Add(
                $"{tool.Alias}.{definition.ValueArgumentName} contains a placeholder or unexpanded variable; runtime authorization remains authoritative.");
            return;
        }

        string path;
        try
        {
            path = DerivePath(step.Args, definition, source);
            if (!Path.IsPathRooted(path))
            {
                return;
            }

            path = Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            warnings.Add(
                $"{tool.Alias}.{definition.ArgumentName} is malformed, so no filesystem grant was inferred.");
            return;
        }

        if (HasSufficientAccess(path, definition.RequiredAccess, currentAccess))
        {
            return;
        }

        var broader = definition.TargetKind == LocalPathTargetKind.FileOrDirectory;
        var directory = definition.TargetKind == LocalPathTargetKind.Directory
            ? path
            : Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.GetPathRoot(path);
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            warnings.Add(
                $"{tool.Alias}.{definition.ArgumentName} could not be classified safely, so no filesystem grant was inferred.");
            return;
        }

        var normalized = ScheduledTaskValidator.NormalizeAccessPath(directory);
        candidates.Add(
            new PathCandidate(
                normalized,
                definition.RequiredAccess,
                tool.Alias,
                broader));
        if (broader)
        {
            warnings.Add(
                $"{tool.Alias}.{definition.ArgumentName} may be a file or directory; the proposed containing directory is broader than the literal target.");
        }
    }

    private static string DerivePath(
        JsonElement arguments,
        LocalPathArgumentDefinition definition,
        string source)
    {
        if (definition.Derivation == LocalPathDerivationKind.Direct)
        {
            return source;
        }

        if (definition.Derivation ==
            LocalPathDerivationKind.OptionalDestinationOrSameDirectoryExtension)
        {
            if (TryGetStringArgument(
                    arguments,
                    definition.ValueArgumentName!,
                    out var optionalDestination))
            {
                return optionalDestination;
            }

            return Path.ChangeExtension(source, definition.DefaultValue);
        }

        if (!TryGetStringArgument(
                arguments,
                definition.ValueArgumentName!,
                out var value) ||
            IsDynamic(value))
        {
            throw new ArgumentException("A derived path argument is unavailable.");
        }

        return definition.Derivation switch
        {
            LocalPathDerivationKind.SameDirectoryLeafName
                when string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) =>
                Path.Combine(Path.GetDirectoryName(source) ?? string.Empty, value),
            LocalPathDerivationKind.SameDirectoryExtension
                when string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) =>
                Path.ChangeExtension(source, value),
            _ => throw new ArgumentException("The derived path argument is invalid."),
        };
    }

    private static bool TryGetStringArgument(
        JsonElement arguments,
        string name,
        out string value)
    {
        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString() ?? string.Empty;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool IsDynamic(string path) =>
        WorkflowPlaceholderCatalog.Extract(path).Count > 0 ||
        path.Contains("{{", StringComparison.Ordinal) ||
        path.Contains("}}", StringComparison.Ordinal) ||
        EnvironmentVariablePattern().IsMatch(path) ||
        path.Contains("$env:", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("${", StringComparison.Ordinal);

    private static bool HasSufficientAccess(
        string absolutePath,
        FileSystemAccessMode requiredAccess,
        IReadOnlyList<FileSystemAccessRule> rules)
    {
        var matching = rules
            .Select(
                static rule => new
                {
                    Rule = rule,
                    Path = ScheduledTaskValidator.NormalizeAccessPath(rule.Path),
                })
            .Where(item => IsWithin(absolutePath, item.Path))
            .OrderByDescending(static item => item.Path.Length)
            .FirstOrDefault();
        return matching is not null &&
            (requiredAccess == FileSystemAccessMode.ReadOnly ||
             matching.Rule.Access == FileSystemAccessMode.ReadWrite);
    }

    private static bool IsWithin(string path, string root)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(path);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        return string.Equals(
                normalizedPath,
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafetyEnabled(string key, SafetyOptions safety) =>
        safety.IsEnabled(key);

    [GeneratedRegex(@"%[^%]+%", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentVariablePattern();

    private sealed record PathCandidate(
        string DirectoryPath,
        FileSystemAccessMode RequiredAccess,
        string Alias,
        bool IsBroaderScope);
}
