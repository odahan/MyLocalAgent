namespace OD.MyLocalAgent.Core;

/// <summary>
/// Summarizes a Task Scheduler resynchronization.
/// </summary>
/// <param name="Registered">The number of enabled tasks registered or refreshed.</param>
/// <param name="Unregistered">The number of disabled or stale registrations removed.</param>
/// <param name="Failures">The task identifiers that could not be synchronized.</param>
public sealed record SchedulerResyncResult(
    int Registered,
    int Unregistered,
    IReadOnlyList<string> Failures);
