using System.Text.Json;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Carries the immutable item and one-based position of one repeated workflow invocation.
/// </summary>
internal readonly record struct WorkflowForEachContext(
    JsonElement Item,
    int Position);
