using FluentAssertions;
using OD.MyLocalAgent.Runner;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies cross-process task execution exclusion remains safe across asynchronous threads.
/// </summary>
public sealed class TaskExecutionMutexTests
{
    /// <summary>
    /// Verifies a held gate blocks a second process view and can be released on another thread.
    /// </summary>
    [Fact]
    public async Task GateCanBeReleasedAfterAsyncContinuation()
    {
        var taskId = $"mutex-test-{Guid.NewGuid():N}";
        var first = new TaskExecutionMutex(taskId);
        using var blocked = new TaskExecutionMutex(taskId);

        first.TryAcquire().Should().BeTrue();
        blocked.TryAcquire().Should().BeFalse();

        await Task.Run(first.Dispose);

        using var next = new TaskExecutionMutex(taskId);
        next.TryAcquire().Should().BeTrue();
    }
}
