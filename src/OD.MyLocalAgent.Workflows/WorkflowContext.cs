using System.Collections.ObjectModel;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Carries ordered named outputs between sequential workflow steps.
/// </summary>
public sealed class WorkflowContext
{
    private readonly Dictionary<string, string> _outputs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, string> _readOnlyOutputs;

    /// <summary>
    /// Initializes an empty workflow context.
    /// </summary>
    public WorkflowContext()
    {
        _readOnlyOutputs = new ReadOnlyDictionary<string, string>(_outputs);
    }

    /// <summary>
    /// Gets the step outputs in insertion order.
    /// </summary>
    public IReadOnlyDictionary<string, string> Outputs => _readOnlyOutputs;

    /// <summary>
    /// Gets the most recently recorded output, or <see langword="null"/> when none exists.
    /// </summary>
    public string? PreviousOutput { get; private set; }

    /// <summary>
    /// Records the successful output of a step.
    /// </summary>
    /// <param name="stepId">The unique workflow step identifier.</param>
    /// <param name="output">The step output.</param>
    public void RecordOutput(string stepId, string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        ArgumentNullException.ThrowIfNull(output);
        if (!_outputs.TryAdd(stepId, output))
        {
            throw new InvalidOperationException(
                $"Workflow output id '{stepId}' has already been recorded.");
        }

        PreviousOutput = output;
    }

    /// <summary>
    /// Attempts to get a previously recorded output.
    /// </summary>
    /// <param name="stepId">The workflow step identifier.</param>
    /// <param name="output">The resolved output when present.</param>
    /// <returns><see langword="true"/> when the output exists.</returns>
    public bool TryGetOutput(string stepId, out string? output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        return _outputs.TryGetValue(stepId, out output);
    }
}
