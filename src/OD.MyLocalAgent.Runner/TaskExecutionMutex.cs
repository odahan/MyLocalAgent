namespace OD.MyLocalAgent.Runner;

internal sealed class TaskExecutionMutex : IDisposable
{
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    public TaskExecutionMutex(string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        _mutex = new Mutex(
            initiallyOwned: false,
            $"Global\\OD.MyLocalAgent.{taskId}");
    }

    public bool TryAcquire()
    {
        try
        {
            _ownsMutex = _mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        return _ownsMutex;
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
