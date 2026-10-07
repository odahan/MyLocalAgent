using System.Collections.ObjectModel;
using System.Text.Json;

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
        RunId = Guid.NewGuid().ToString("N");
    }

    /// <summary>Gets the stable identifier of this workflow execution.</summary>
    public string RunId { get; }
    /// <summary>The effective decision configuration captured before the first step.</summary>
    public OD.MyLocalAgent.Core.DecisionExecutionSnapshot? DecisionSnapshot { get; init; }

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

    /// <summary>
    /// Creates a JSON snapshot of every successful step output.
    /// JSON outputs remain structured while plain-text outputs remain strings.
    /// </summary>
    /// <returns>The serialized workflow context.</returns>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("steps");
            writer.WriteStartObject();
            foreach (var (stepId, output) in _outputs)
            {
                writer.WritePropertyName(stepId);
                WriteOutput(writer, output);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteOutput(Utf8JsonWriter writer, string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            document.RootElement.WriteTo(writer);
        }
        catch (JsonException)
        {
            writer.WriteStringValue(output);
        }
    }
}
