namespace OD.MyLocalAgent.Runner;

/// <summary>
/// Coordinates a scheduled task across processes so the same task cannot run concurrently.
/// </summary>
internal sealed class TaskExecutionMutex : IDisposable
{
    private readonly Semaphore _semaphore;
    private bool _ownsSemaphore;

    public TaskExecutionMutex(string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        _semaphore = new Semaphore(
            initialCount: 1,
            maximumCount: 1,
            // A global name also covers runner processes started in other Windows sessions.
            $"Global\\OD.MyLocalAgent.{taskId}");
    }

    public bool TryAcquire()
    {
        _ownsSemaphore = _semaphore.WaitOne(TimeSpan.Zero);
        return _ownsSemaphore;
    }

    public void Dispose()
    {
        if (_ownsSemaphore)
        {
            _ = _semaphore.Release();
            _ownsSemaphore = false;
        }

        _semaphore.Dispose();
    }
}
