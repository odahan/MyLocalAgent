namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Collects per-operation timing entries during a single task execution.
/// </summary>
/// <remarks>
/// Set <see cref="Current"/> before starting a task execution so that
/// tool and guardrail call sites can record timings automatically without
/// requiring any changes at the call sites.
/// </remarks>
public sealed class TaskTimingCollector
{
    /// <summary>Category constant for AI prompt calls.</summary>
    public const string AiPromptCategory = "ai.prompt";

    /// <summary>Category constant for tool calls.</summary>
    public const string ToolCategory = "tool";

    /// <summary>Category constant for guardrail calls.</summary>
    public const string GuardrailCategory = "guardrail";

    /// <summary>
    /// Gets or sets the ambient collector for the current async execution context.
    /// Set this to a new instance before starting a task; read it after completion.
    /// </summary>
    public static readonly AsyncLocal<TaskTimingCollector?> Current = new();

    private readonly List<TaskTimingEntry> _entries = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Records one timed operation.
    /// </summary>
    /// <param name="category">The stable category.</param>
    /// <param name="name">The operation name.</param>
    /// <param name="duration">The measured wall-clock duration.</param>
    public void Record(string category, string name, TimeSpan duration)
    {
        var entry = new TaskTimingEntry(category, name, duration);
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>
    /// Returns a snapshot of all recorded entries in insertion order.
    /// </summary>
    public IReadOnlyList<TaskTimingEntry> GetEntries()
    {
        lock (_gate)
        {
            return [.. _entries];
        }
    }
}
